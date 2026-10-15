using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Chat;
using NovaKnowledge.Contracts.Common;
using NovaKnowledge.Contracts.Search;
using NovaKnowledge.Domain.Entities;
using NovaKnowledge.Infrastructure.Caching;
using NovaKnowledge.Infrastructure.Chat;
using NovaKnowledge.Infrastructure.Configuration;

namespace NovaKnowledge.Chat.Api.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly IConversationRepository _conversations;
    private readonly ISearchQueryService _search;
    private readonly IChatCompletionService _chat;
    private readonly IAgentOrchestrator _orchestrator;
    private readonly IPromptTemplateService _prompts;
    private readonly ITenantSettingsRepository _tenantSettings;
    private readonly ICacheService _cache;
    private readonly AzureOpenAIOptions _openAiOptions;
    private readonly PricingOptions _pricing;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IConversationRepository conversations,
        ISearchQueryService search,
        IChatCompletionService chat,
        IAgentOrchestrator orchestrator,
        IPromptTemplateService prompts,
        ITenantSettingsRepository tenantSettings,
        ICacheService cache,
        IOptions<AzureOpenAIOptions> openAiOptions,
        IOptions<PricingOptions> pricing,
        ILogger<ChatController> logger)
    {
        _conversations = conversations;
        _search = search;
        _chat = chat;
        _orchestrator = orchestrator;
        _prompts = prompts;
        _tenantSettings = tenantSettings;
        _cache = cache;
        _openAiOptions = openAiOptions.Value;
        _pricing = pricing.Value;
        _logger = logger;
    }

    [HttpPost("sessions")]
    public async Task<ActionResult<ApiResponse<SessionResponse>>> CreateSession(
        [FromBody] CreateSessionRequest request,
        CancellationToken cancellationToken)
    {
        var session = new Conversation
        {
            TenantId = request.TenantId,
            UserId = request.UserId,
            Title = "New conversation"
        };
        await _conversations.AddAsync(session, cancellationToken);
        await _conversations.SaveChangesAsync(cancellationToken);
        return Created($"/api/chat/sessions/{session.Id}",
            new ApiResponse<SessionResponse>(new SessionResponse(session.Id, session.TenantId, session.Title, session.CreatedAt), HttpContext.TraceIdentifier));
    }

    [HttpGet("sessions/{id:guid}/messages")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MessageResponse>>>> GetMessages(
        Guid id,
        CancellationToken cancellationToken)
    {
        var conversation = await _conversations.GetWithMessagesAsync(id, cancellationToken);
        if (conversation == null) return NotFound(new ErrorResponse("Session not found"));

        var messages = conversation.Messages.Select(MapMessage).ToList();
        return Ok(new ApiResponse<IReadOnlyList<MessageResponse>>(messages, HttpContext.TraceIdentifier));
    }

    [HttpGet("sessions/{id:guid}/trace")]
    public async Task<ActionResult<ApiResponse<AgentTraceResponse>>> GetTrace(
        Guid id,
        [FromQuery] Guid? messageId,
        CancellationToken cancellationToken)
    {
        var runs = await _conversations.GetAgentRunsAsync(id, messageId, cancellationToken);
        var steps = runs.Select(r => new AgentTraceStep(r.AgentName, r.Input, r.Output, r.DurationMs, r.OrderIndex)).ToList();
        return Ok(new ApiResponse<AgentTraceResponse>(new AgentTraceResponse(id, messageId, steps), HttpContext.TraceIdentifier));
    }

    [HttpPost("sessions/{id:guid}/messages")]
    public async Task SendMessage(
        Guid id,
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _conversations.GetSessionContextAsync(id, cancellationToken);
        if (session == null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(new ErrorResponse("Session not found"), cancellationToken);
            return;
        }

        var conversationId = session.Value.Id;
        var tenantId = session.Value.TenantId;

        ChatStreamWriter.Prepare(Response);

        var userMessage = new Message
        {
            Role = MessageRole.User,
            Content = request.Content
        };
        await _conversations.AddMessageAsync(conversationId, userMessage, cancellationToken);
        await _conversations.SaveChangesAsync(cancellationToken);

        var settings = await _tenantSettings.GetByTenantAsync(tenantId, cancellationToken)
            ?? new TenantSettings { TenantId = tenantId };

        var template = await _prompts.GetActiveTemplateAsync("general", tenantId, cancellationToken);
        var systemPrompt = template?.SystemPrompt ?? """
            You are NovaKnowledge, Contoso Corp's enterprise knowledge copilot.
            Answer using provided context only. Cite sources with [n] notation.
            """;

        var useHybrid = true;
        var useSemanticRanker = true;

        // Retrieval with a stable, process-independent cache key.
        var cacheKey = CacheKeys.Search(tenantId, request.Content, settings.RetrievalTopK, useHybrid, useSemanticRanker);
        SearchQueryResponse? searchResult = await _cache.GetAsync<SearchQueryResponse>(cacheKey, cancellationToken);
        var retrievalSw = System.Diagnostics.Stopwatch.StartNew();
        if (searchResult == null)
        {
            searchResult = await _search.SearchAsync(new SearchQueryRequest(
                request.Content, tenantId, settings.RetrievalTopK, UseHybrid: useHybrid, UseSemanticRanker: useSemanticRanker), cancellationToken);
            await _cache.SetAsync(cacheKey, searchResult, TimeSpan.FromMinutes(10), cancellationToken);
        }
        retrievalSw.Stop();

        var citations = searchResult.Results.Select(r => new CitationDto(
            r.DocumentId, r.ChunkIndex, r.Content, r.Title, r.Score)).ToList();

        await ChatStreamWriter.WriteAsync(Response, new ChatStreamChunk("citations", Citations: citations), cancellationToken);

        string answer;
        int promptTokens = 0, completionTokens = 0, llmMs = 0;
        List<AgentRun>? agentRuns = null;

        try
        {
            if (settings.UseMultiAgent)
            {
                var orchResult = await _orchestrator.OrchestrateAsync(new AgentOrchestrationRequest(
                    request.Content, tenantId, conversationId, searchResult.Results, systemPrompt), cancellationToken);

                answer = orchResult.FinalAnswer;
                promptTokens = orchResult.PromptTokens;
                completionTokens = orchResult.CompletionTokens;
                llmMs = orchResult.TotalLlmLatencyMs;

                var order = 0;
                agentRuns = orchResult.Trace.Select(step => new AgentRun
                {
                    ConversationId = conversationId,
                    AgentName = step.AgentName,
                    Input = step.Input,
                    Output = step.Output,
                    DurationMs = step.DurationMs,
                    OrderIndex = order++
                }).ToList();

                // Orchestration produces a complete answer; emit it as a single token event.
                await ChatStreamWriter.WriteAsync(Response, new ChatStreamChunk("token", Content: answer), cancellationToken);
            }
            else
            {
                var context = string.Join("\n\n", searchResult.Results.Select((c, i) => $"[{i + 1}] {c.Content}"));
                var userPrompt = $"Context:\n{context}\n\nQuestion: {request.Content}";
                var llmSw = System.Diagnostics.Stopwatch.StartNew();
                var builder = new System.Text.StringBuilder();

                await foreach (var update in _chat.StreamAsync(systemPrompt, userPrompt, cancellationToken))
                {
                    if (!string.IsNullOrEmpty(update.TokenDelta))
                    {
                        builder.Append(update.TokenDelta);
                        await ChatStreamWriter.WriteAsync(Response, new ChatStreamChunk("token", Content: update.TokenDelta), cancellationToken);
                    }

                    if (update.IsFinal)
                    {
                        promptTokens = update.PromptTokens ?? 0;
                        completionTokens = update.CompletionTokens ?? 0;
                    }
                }

                llmSw.Stop();
                llmMs = (int)llmSw.ElapsedMilliseconds;
                answer = builder.ToString();
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "LLM call failed for session {SessionId}", id);
            await ChatStreamWriter.WriteAsync(Response, new ChatStreamChunk("error", Content: GetUserFriendlyError(ex)), cancellationToken);
            return;
        }

        var assistantMessage = new Message
        {
            Role = MessageRole.Assistant,
            Content = answer,
            CitationsJson = JsonSerializer.Serialize(citations),
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            RetrievalLatencyMs = (int)retrievalSw.ElapsedMilliseconds,
            LlmLatencyMs = llmMs,
            TotalLatencyMs = (int)retrievalSw.ElapsedMilliseconds + llmMs,
            EstimatedCostUsd = EstimateCost(promptTokens, completionTokens)
        };
        await _conversations.AddMessageAsync(conversationId, assistantMessage, cancellationToken);
        await _conversations.SaveChangesAsync(cancellationToken);

        // Persist agent trace steps linked to the assistant message so /trace?messageId works.
        if (agentRuns is { Count: > 0 })
        {
            foreach (var run in agentRuns)
            {
                run.MessageId = assistantMessage.Id;
                await _conversations.AddAgentRunAsync(run, cancellationToken);
            }
            await _conversations.SaveChangesAsync(cancellationToken);
        }

        ChatStreamWriter.RecordTurnMetrics(
            assistantMessage.RetrievalLatencyMs, llmMs, promptTokens, completionTokens, assistantMessage.EstimatedCostUsd);

        await ChatStreamWriter.WriteAsync(Response, new ChatStreamChunk("done", Message: MapMessage(assistantMessage)), cancellationToken);
    }

    private static bool IsAzureDeploymentError(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("DeploymentNotFound", StringComparison.OrdinalIgnoreCase)
                || message.Contains("404", StringComparison.OrdinalIgnoreCase)
                || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string GetUserFriendlyError(Exception ex)
    {
        if (IsAzureDeploymentError(ex))
            return "The configured Azure OpenAI chat deployment was not found. Deploy a chat model and set AzureOpenAI:ChatDeployment.";

        return "The chat request failed. Please try again or contact support.";
    }

    private decimal EstimateCost(int promptTokens, int completionTokens) =>
        (promptTokens / 1000m * _pricing.PromptCostPer1KTokens)
        + (completionTokens / 1000m * _pricing.CompletionCostPer1KTokens);

    private static MessageResponse MapMessage(Message m)
    {
        IReadOnlyList<CitationDto>? citations = null;
        if (!string.IsNullOrEmpty(m.CitationsJson))
            citations = JsonSerializer.Deserialize<IReadOnlyList<CitationDto>>(m.CitationsJson);

        return new MessageResponse(
            m.Id, m.Role.ToString(), m.Content, citations,
            m.PromptTokens, m.CompletionTokens, m.TotalLatencyMs, m.CreatedAt);
    }
}
