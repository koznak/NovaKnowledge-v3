using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Admin;

namespace NovaKnowledge.Infrastructure.Agents;

/// <summary>Triggers RAG evaluation runs on the Python evaluation service and tracks their status.</summary>
public sealed class HttpEvaluationClient : IEvaluationClient
{
    private readonly HttpClient _http;
    private readonly IEvaluationRunRepository _repository;
    private readonly ILogger<HttpEvaluationClient> _logger;

    public HttpEvaluationClient(HttpClient http, IEvaluationRunRepository repository, ILogger<HttpEvaluationClient> logger)
    {
        _http = http;
        _repository = repository;
        _logger = logger;
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
            var response = await _http.PostAsJsonAsync(
                "/evaluate/rag",
                new { run_id = run.Id, dataset_path = run.DatasetPath },
                cancellationToken);
            response.EnsureSuccessStatusCode();
            run.Status = Domain.Entities.EvaluationStatus.Running;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to trigger evaluation run {RunId} on the evaluation service", run.Id);
            run.Status = Domain.Entities.EvaluationStatus.Failed;
            run.ErrorMessage = "Failed to trigger evaluation service";
        }

        await _repository.UpdateAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    public async Task<EvaluationRunDto?> GetEvaluationStatusAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await _repository.GetByIdAsync(runId, cancellationToken);
        if (run == null) return null;
        return new EvaluationRunDto(
            run.Id, run.Status.ToString(), run.FaithfulnessScore, run.AnswerRelevanceScore,
            run.ContextPrecisionScore, run.ContextRecallScore, run.CreatedAt, run.CompletedAt, run.ErrorMessage);
    }
}
