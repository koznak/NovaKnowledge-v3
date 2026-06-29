using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Chat;

namespace NovaKnowledge.Infrastructure.Agents;

public class SemanticKernelAgentOrchestrator : IAgentOrchestrator
{
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<SemanticKernelAgentOrchestrator> _logger;

    public SemanticKernelAgentOrchestrator(
        IChatCompletionService chatService,
        ILogger<SemanticKernelAgentOrchestrator> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    public async Task<AgentOrchestrationResult> OrchestrateAsync(
        AgentOrchestrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var trace = new List<AgentTraceStep>();
        var order = 0;
        var totalPromptTokens = 0;
        var totalCompletionTokens = 0;
        var totalLlmMs = 0;

        // Router Agent
        var routerSw = Stopwatch.StartNew();
        var routerPrompt = $"""
            Classify the user intent as one of: policy_qa, summarization, out_of_scope.
            User message: {request.UserMessage}
            Respond with only the intent label.
            """;
        var (routerOutput, rPrompt, rCompletion) = await _chatService.CompleteAsync(
            "You are a routing agent for an enterprise knowledge copilot.",
            routerPrompt,
            cancellationToken);
        routerSw.Stop();
        trace.Add(new AgentTraceStep("RouterAgent", request.UserMessage, routerOutput.Trim(), (int)routerSw.ElapsedMilliseconds, order++));
        totalPromptTokens += rPrompt;
        totalCompletionTokens += rCompletion;
        totalLlmMs += (int)routerSw.ElapsedMilliseconds;

        if (routerOutput.Contains("out_of_scope", StringComparison.OrdinalIgnoreCase))
        {
            const string oos = "I'm designed to answer questions about Contoso Corp HR, IT, and policy documents. Please rephrase your question within that scope.";
            trace.Add(new AgentTraceStep("SynthesisAgent", "out_of_scope", oos, 0, order));
            return new AgentOrchestrationResult(oos, trace, totalLlmMs, totalPromptTokens, totalCompletionTokens);
        }

        // Research Agent (context already retrieved; format citations)
        var researchSw = Stopwatch.StartNew();
        var context = string.Join("\n\n", request.RetrievedChunks.Select((c, i) =>
            $"[{i + 1}] {c.Title ?? "Document"} (score: {c.Score:F2})\n{c.Content}"));
        var researchOutput = $"Retrieved {request.RetrievedChunks.Count} relevant chunks.";
        researchSw.Stop();
        trace.Add(new AgentTraceStep("ResearchAgent", request.UserMessage, researchOutput, (int)researchSw.ElapsedMilliseconds, order++));

        // Synthesis Agent
        var synthesisSw = Stopwatch.StartNew();
        var synthesisUserPrompt = $"""
            Context:
            {context}

            User question: {request.UserMessage}

            Answer using only the provided context. Include citation numbers like [1], [2].
            If the context is insufficient, say so clearly.
            """;
        var (answer, sPrompt, sCompletion) = await _chatService.CompleteAsync(
            request.SystemPrompt,
            synthesisUserPrompt,
            cancellationToken);
        synthesisSw.Stop();
        trace.Add(new AgentTraceStep("SynthesisAgent", synthesisUserPrompt, answer, (int)synthesisSw.ElapsedMilliseconds, order++));
        totalPromptTokens += sPrompt;
        totalCompletionTokens += sCompletion;
        totalLlmMs += (int)synthesisSw.ElapsedMilliseconds;

        // Compliance Agent
        var complianceSw = Stopwatch.StartNew();
        var (complianceOutput, cPrompt, cCompletion) = await _chatService.CompleteAsync(
            """
            You are a compliance reviewer. Ensure the answer:
            1. Does not invent policy details not in context
            2. Includes a brief disclaimer that official HR/IT should verify critical decisions
            Return the revised answer only.
            """,
            answer,
            cancellationToken);
        complianceSw.Stop();
        var finalAnswer = string.IsNullOrWhiteSpace(complianceOutput) ? answer : complianceOutput;
        trace.Add(new AgentTraceStep("ComplianceAgent", answer, finalAnswer, (int)complianceSw.ElapsedMilliseconds, order++));
        totalPromptTokens += cPrompt;
        totalCompletionTokens += cCompletion;
        totalLlmMs += (int)complianceSw.ElapsedMilliseconds;

        _logger.LogInformation("Multi-agent orchestration completed with {StepCount} steps", trace.Count);
        return new AgentOrchestrationResult(finalAnswer, trace, totalLlmMs, totalPromptTokens, totalCompletionTokens);
    }
}

public class PromptTemplateService(IPromptTemplateRepository repository) : IPromptTemplateService
{
    public Task<Domain.Entities.PromptTemplate?> GetActiveTemplateAsync(string category, string tenantId, CancellationToken cancellationToken = default) =>
        repository.GetActiveAsync(category, tenantId, cancellationToken);
}

public class HttpEvaluationClient : IEvaluationClient
{
    private readonly HttpClient _http;
    private readonly IEvaluationRunRepository _repository;

    public HttpEvaluationClient(HttpClient http, IEvaluationRunRepository repository)
    {
        _http = http;
        _repository = repository;
    }

    public async Task<Guid> TriggerEvaluationAsync(string? datasetPath, CancellationToken cancellationToken = default)
    {
        var run = new Domain.Entities.EvaluationRun
        {
            DatasetPath = datasetPath ?? "tests/data/golden-qa.jsonl",
            Status = Domain.Entities.EvaluationStatus.Pending
        };
        await _repository.AddAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            var response = await _http.PostAsJsonAsync("/evaluate/rag", new { run_id = run.Id, dataset_path = run.DatasetPath }, cancellationToken);
            response.EnsureSuccessStatusCode();
            run.Status = Domain.Entities.EvaluationStatus.Running;
        }
        catch
        {
            run.Status = Domain.Entities.EvaluationStatus.Failed;
            run.ErrorMessage = "Failed to trigger evaluation service";
        }

        await _repository.UpdateAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    public async Task<Contracts.Admin.EvaluationRunDto?> GetEvaluationStatusAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await _repository.GetByIdAsync(runId, cancellationToken);
        if (run == null) return null;
        return new Contracts.Admin.EvaluationRunDto(
            run.Id, run.Status.ToString(), run.FaithfulnessScore, run.AnswerRelevanceScore,
            run.ContextPrecisionScore, run.ContextRecallScore, run.CreatedAt, run.CompletedAt, run.ErrorMessage);
    }
}
