using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Chat;
using NovaKnowledge.Contracts.Common;
using NovaKnowledge.Contracts.Search;
using NovaKnowledge.Domain.Entities;
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
    private readonly HttpClient _orchestrationHttp;
    private readonly IWebHostEnvironment _environment;
    private readonly AzureOpenAIOptions _openAiOptions;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IConversationRepository conversations,
        ISearchQueryService search,
        IChatCompletionService chat,
        IAgentOrchestrator orchestrator,
        IPromptTemplateService prompts,
        ITenantSettingsRepository tenantSettings,
        ICacheService cache,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment environment,
        IOptions<AzureOpenAIOptions> openAiOptions,
        ILogger<ChatController> logger)
    {
        _conversations = conversations;
        _search = search;
        _chat = chat;
        _orchestrator = orchestrator;
        _prompts = prompts;
        _tenantSettings = tenantSettings;
        _cache = cache;
        _orchestrationHttp = httpClientFactory.CreateClient("orchestration");
        _environment = environment;
        _openAiOptions = openAiOptions.Value;
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

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

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

        // Retrieval with cache
        var cacheKey = $"search:{tenantId}:{request.Content.GetHashCode()}";
        SearchQueryResponse? searchResult = await _cache.GetAsync<SearchQueryResponse>(cacheKey, cancellationToken);
        var retrievalSw = System.Diagnostics.Stopwatch.StartNew();
        if (searchResult == null)
        {
            searchResult = await _search.SearchAsync(new SearchQueryRequest(
                request.Content, tenantId, settings.RetrievalTopK, UseHybrid: true), cancellationToken);
            await _cache.SetAsync(cacheKey, searchResult, TimeSpan.FromMinutes(10), cancellationToken);
        }
        retrievalSw.Stop();

        var citations = searchResult.Results.Select(r => new CitationDto(
            r.DocumentId, r.ChunkIndex, r.Content, r.Title, r.Score)).ToList();

        await WriteSseAsync(new ChatStreamChunk("citations", Citations: citations), cancellationToken);

        string answer;
        int promptTokens, completionTokens, llmMs;

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
                foreach (var step in orchResult.Trace)
                {
                    await _conversations.AddAgentRunAsync(new AgentRun
                    {
                        ConversationId = conversationId,
                        MessageId = null,
                        AgentName = step.AgentName,
                        Input = step.Input,
                        Output = step.Output,
                        DurationMs = step.DurationMs,
                        OrderIndex = order++
                    }, cancellationToken);
                }
            }
            else
            {
                var context = string.Join("\n\n", searchResult.Results.Select((c, i) => $"[{i + 1}] {c.Content}"));
                var userPrompt = $"Context:\n{context}\n\nQuestion: {request.Content}";
                var llmSw = System.Diagnostics.Stopwatch.StartNew();
                (answer, promptTokens, completionTokens) = await _chat.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
                llmMs = (int)llmSw.ElapsedMilliseconds;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "LLM call failed for session {SessionId}", id);

            if (_environment.IsDevelopment() && IsAzureDeploymentError(ex))
            {
                answer = BuildDevelopmentFallbackAnswer(request.Content, citations);
                promptTokens = 0;
                completionTokens = 0;
                llmMs = 0;
            }
            else
            {
                await WriteSseAsync(new ChatStreamChunk("error", Content: GetUserFriendlyError(ex)), cancellationToken);
                return;
            }
        }

        foreach (var chunk in ChunkText(answer, 32))
        {
            await WriteSseAsync(new ChatStreamChunk("token", Content: chunk), cancellationToken);
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

        await WriteSseAsync(new ChatStreamChunk("done", Message: MapMessage(assistantMessage)), cancellationToken);
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

    private string BuildDevelopmentFallbackAnswer(string question, IReadOnlyList<CitationDto> citations)
    {
        var citationNote = citations.Count > 0
            ? $"Found {citations.Count} relevant document chunk(s)."
            : "No documents are indexed yet, so retrieval returned no citations.";

        return $"""
            [Development fallback] Azure OpenAI chat deployment '{_openAiOptions.ChatDeployment}' is not available on your resource.

            {citationNote}

            Your question: {question}

            To enable live answers:
            1. In Azure AI Foundry, deploy a chat model (recommended: gpt-5-mini) with deployment name matching AzureOpenAI:ChatDeployment.
            2. Restart Chat.Api and Orchestration.Api.
            3. Upload HR/IT PDFs via Ingestion.Api so search returns citations.
            """;
    }

    private static string GetUserFriendlyError(Exception ex)
    {
        if (IsAzureDeploymentError(ex))
            return "Azure OpenAI chat deployment not found. Deploy gpt-5-mini in Azure AI Foundry and set AzureOpenAI:ChatDeployment.";

        return $"Chat failed: {ex.Message}";
    }

    private async Task WriteSseAsync(ChatStreamChunk chunk, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(chunk);
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static IEnumerable<string> ChunkText(string text, int size)
    {
        for (var i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }

    private static decimal EstimateCost(int promptTokens, int completionTokens) =>
        promptTokens * 0.00000015m + completionTokens * 0.0000006m;

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
