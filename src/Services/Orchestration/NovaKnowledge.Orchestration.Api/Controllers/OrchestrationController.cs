using Microsoft.AspNetCore.Mvc;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Chat;
using NovaKnowledge.Contracts.Common;
using NovaKnowledge.Contracts.Search;

namespace NovaKnowledge.Orchestration.Api.Controllers;

[ApiController]
[Route("api/orchestration")]
public class OrchestrationController : ControllerBase
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ISearchQueryService _search;
    private readonly IPromptTemplateService _prompts;

    public OrchestrationController(
        IAgentOrchestrator orchestrator,
        ISearchQueryService search,
        IPromptTemplateService prompts)
    {
        _orchestrator = orchestrator;
        _search = search;
        _prompts = prompts;
    }

    public record OrchestrateRequest(string Message, string TenantId, Guid ConversationId, int TopK = 5);

    public record OrchestrateResponse(
        string Answer,
        IReadOnlyList<AgentTraceStep> Trace,
        IReadOnlyList<SearchResultItem> Sources,
        int TotalLlmLatencyMs,
        int PromptTokens,
        int CompletionTokens);

    [HttpPost("run")]
    public async Task<ActionResult<ApiResponse<OrchestrateResponse>>> Run(
        [FromBody] OrchestrateRequest request,
        CancellationToken cancellationToken)
    {
        var searchResult = await _search.SearchAsync(
            new SearchQueryRequest(request.Message, request.TenantId, request.TopK, UseHybrid: true),
            cancellationToken);

        var template = await _prompts.GetActiveTemplateAsync("general", request.TenantId, cancellationToken);
        var systemPrompt = template?.SystemPrompt ?? "You are NovaKnowledge, Contoso Corp's enterprise knowledge copilot.";

        var result = await _orchestrator.OrchestrateAsync(
            new AgentOrchestrationRequest(request.Message, request.TenantId, request.ConversationId,
                searchResult.Results, systemPrompt),
            cancellationToken);

        return Ok(new ApiResponse<OrchestrateResponse>(
            new OrchestrateResponse(
                result.FinalAnswer, result.Trace, searchResult.Results,
                result.TotalLlmLatencyMs, result.PromptTokens, result.CompletionTokens),
            HttpContext.TraceIdentifier));
    }
}
