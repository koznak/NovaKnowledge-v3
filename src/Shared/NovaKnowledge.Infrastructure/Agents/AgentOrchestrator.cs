using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Chat;

namespace NovaKnowledge.Infrastructure.Agents;

/// <summary>
/// Sequential multi-agent pipeline built on Microsoft Semantic Kernel agents:
/// Router -> Research -> Synthesis -> Compliance.
/// Router, Synthesis and Compliance are <see cref="ChatCompletionAgent"/> instances;
/// Research is a deterministic context-formatting step (no LLM call), matching ADR-002.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private const string RouterInstructions =
        "You are the routing agent for an enterprise knowledge copilot. " +
        "Classify the user's intent as exactly one of: policy_qa, summarization, out_of_scope. " +
        "Respond with only the intent label, nothing else.";

    private const string ComplianceInstructions =
        "You are a compliance reviewer. Revise the answer so that it: " +
        "(1) does not invent policy details that are not present in the provided context, and " +
        "(2) ends with a brief note that official HR/IT should verify critical decisions. " +
        "Return the revised answer only.";

    private const string OutOfScopeResponse =
        "I'm designed to answer questions about Contoso Corp HR, IT, and policy documents. " +
        "Please rephrase your question within that scope.";

    private readonly Kernel _kernel;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(Kernel kernel, ILogger<AgentOrchestrator> logger)
    {
        _kernel = kernel;
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

        var thread = new ChatHistoryAgentThread();

        // 1. Router agent — intent classification.
        var router = CreateAgent("RouterAgent", RouterInstructions);
        var routerInput = $"User message: {request.UserMessage}";
        var (intent, routerPrompt, routerCompletion, routerMs) =
            await InvokeAsync(router, routerInput, thread, cancellationToken);

        trace.Add(new AgentTraceStep("RouterAgent", routerInput, intent, routerMs, order++));
        totalPromptTokens += routerPrompt;
        totalCompletionTokens += routerCompletion;
        totalLlmMs += routerMs;

        if (intent.Contains("out_of_scope", StringComparison.OrdinalIgnoreCase))
        {
            trace.Add(new AgentTraceStep("SynthesisAgent", "out_of_scope", OutOfScopeResponse, 0, order));
            return new AgentOrchestrationResult(OutOfScopeResponse, trace, totalLlmMs, totalPromptTokens, totalCompletionTokens);
        }

        // 2. Research step — deterministic formatting of retrieved chunks into citations.
        var researchSw = Stopwatch.StartNew();
        var context = FormatContext(request.RetrievedChunks);
        var researchOutput = $"Retrieved {request.RetrievedChunks.Count} relevant chunk(s).";
        researchSw.Stop();
        trace.Add(new AgentTraceStep("ResearchAgent", request.UserMessage, researchOutput, (int)researchSw.ElapsedMilliseconds, order++));

        // 3. Synthesis agent — grounded, cited answer.
        var synthesis = CreateAgent("SynthesisAgent", request.SystemPrompt);
        var synthesisInput =
            $"Context:\n{context}\n\nUser question: {request.UserMessage}\n\n" +
            "Answer using only the provided context. Include citation numbers like [1], [2]. " +
            "If the context is insufficient, say so clearly.";
        var (answer, synthesisPrompt, synthesisCompletion, synthesisMs) =
            await InvokeAsync(synthesis, synthesisInput, thread, cancellationToken);

        trace.Add(new AgentTraceStep("SynthesisAgent", synthesisInput, answer, synthesisMs, order++));
        totalPromptTokens += synthesisPrompt;
        totalCompletionTokens += synthesisCompletion;
        totalLlmMs += synthesisMs;

        // 4. Compliance agent — guardrails and disclaimer.
        var compliance = CreateAgent("ComplianceAgent", ComplianceInstructions);
        var (revised, compliancePrompt, complianceCompletion, complianceMs) =
            await InvokeAsync(compliance, answer, thread, cancellationToken);

        var finalAnswer = string.IsNullOrWhiteSpace(revised) ? answer : revised;
        trace.Add(new AgentTraceStep("ComplianceAgent", answer, finalAnswer, complianceMs, order));
        totalPromptTokens += compliancePrompt;
        totalCompletionTokens += complianceCompletion;
        totalLlmMs += complianceMs;

        _logger.LogInformation(
            "Multi-agent orchestration completed: {StepCount} steps, {PromptTokens}+{CompletionTokens} tokens",
            trace.Count, totalPromptTokens, totalCompletionTokens);

        return new AgentOrchestrationResult(finalAnswer, trace, totalLlmMs, totalPromptTokens, totalCompletionTokens);
    }

    private ChatCompletionAgent CreateAgent(string name, string instructions) => new()
    {
        Name = name,
        Instructions = instructions,
        Kernel = _kernel,
        Arguments = []
    };

    private static async Task<(string Text, int PromptTokens, int CompletionTokens, int ElapsedMs)> InvokeAsync(
        ChatCompletionAgent agent,
        string input,
        ChatHistoryAgentThread thread,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var builder = new StringBuilder();
        var promptTokens = 0;
        var completionTokens = 0;

        await foreach (var response in agent.InvokeAsync(input, thread, options: null, cancellationToken: cancellationToken))
        {
            builder.Append(response.Message.Content);
            var (prompt, completion) = ReadUsage(response.Message);
            promptTokens += prompt;
            completionTokens += completion;
        }

        sw.Stop();
        return (builder.ToString().Trim(), promptTokens, completionTokens, (int)sw.ElapsedMilliseconds);
    }

    private static string FormatContext(IReadOnlyList<Contracts.Search.SearchResultItem> chunks) =>
        string.Join("\n\n", chunks.Select((c, i) =>
            $"[{i + 1}] {c.Title ?? "Document"} (score: {c.Score:F2})\n{c.Content}"));

    private static (int PromptTokens, int CompletionTokens) ReadUsage(ChatMessageContent message)
    {
        if (message.Metadata is { } metadata
            && metadata.TryGetValue("Usage", out var usage)
            && usage is UsageDetails details)
        {
            return ((int)(details.InputTokenCount ?? 0), (int)(details.OutputTokenCount ?? 0));
        }

        return (0, 0);
    }
}
