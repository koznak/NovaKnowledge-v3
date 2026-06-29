using Microsoft.AspNetCore.Mvc;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Admin;
using NovaKnowledge.Contracts.Common;
using NovaKnowledge.Domain.Entities;

namespace NovaKnowledge.Admin.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IPromptTemplateRepository _prompts;
    private readonly IUsageMetricsRepository _metrics;
    private readonly IEvaluationClient _evaluation;
    private readonly IEvaluationRunRepository _evaluationRuns;
    private readonly ITenantSettingsRepository _tenantSettings;
    private readonly IDocumentRepository _documents;
    private readonly IMessagePublisher _publisher;
    private readonly NovaKnowledge.Infrastructure.Configuration.NovaKnowledgeOptions _options;

    public AdminController(
        IPromptTemplateRepository prompts,
        IUsageMetricsRepository metrics,
        IEvaluationClient evaluation,
        IEvaluationRunRepository evaluationRuns,
        ITenantSettingsRepository tenantSettings,
        IDocumentRepository documents,
        IMessagePublisher publisher,
        Microsoft.Extensions.Options.IOptions<NovaKnowledge.Infrastructure.Configuration.NovaKnowledgeOptions> options)
    {
        _prompts = prompts;
        _metrics = metrics;
        _evaluation = evaluation;
        _evaluationRuns = evaluationRuns;
        _tenantSettings = tenantSettings;
        _documents = documents;
        _publisher = publisher;
        _options = options.Value;
    }

    [HttpGet("prompts")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PromptTemplateDto>>>> ListPrompts(CancellationToken cancellationToken)
    {
        var items = await _prompts.ListAsync(_options.DefaultTenantId, cancellationToken);
        return Ok(new ApiResponse<IReadOnlyList<PromptTemplateDto>>(
            items.Select(p => new PromptTemplateDto(p.Id, p.Name, p.Category, p.Version, p.SystemPrompt, p.UserPromptTemplate, p.IsActive, p.AbTestTag)).ToList(),
            HttpContext.TraceIdentifier));
    }

    [HttpPost("prompts")]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> CreatePrompt(
        [FromBody] CreatePromptTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var template = new PromptTemplate
        {
            TenantId = _options.DefaultTenantId,
            Name = request.Name,
            Category = request.Category,
            SystemPrompt = request.SystemPrompt,
            UserPromptTemplate = request.UserPromptTemplate,
            AbTestTag = request.AbTestTag,
            Version = 1,
            IsActive = true
        };
        await _prompts.AddAsync(template, cancellationToken);
        await _prompts.SaveChangesAsync(cancellationToken);
        return Created($"/api/admin/prompts/{template.Id}",
            new ApiResponse<PromptTemplateDto>(
                new PromptTemplateDto(template.Id, template.Name, template.Category, template.Version,
                    template.SystemPrompt, template.UserPromptTemplate, template.IsActive, template.AbTestTag),
                HttpContext.TraceIdentifier));
    }

    [HttpPut("prompts/{id:guid}")]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> UpdatePrompt(
        Guid id,
        [FromBody] UpdatePromptTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var template = await _prompts.GetByIdAsync(id, cancellationToken);
        if (template == null) return NotFound(new ErrorResponse("Prompt not found"));

        template.SystemPrompt = request.SystemPrompt;
        template.UserPromptTemplate = request.UserPromptTemplate;
        template.IsActive = request.IsActive;
        template.Version++;
        await _prompts.UpdateAsync(template, cancellationToken);
        await _prompts.SaveChangesAsync(cancellationToken);

        return Ok(new ApiResponse<PromptTemplateDto>(
            new PromptTemplateDto(template.Id, template.Name, template.Category, template.Version,
                template.SystemPrompt, template.UserPromptTemplate, template.IsActive, template.AbTestTag),
            HttpContext.TraceIdentifier));
    }

    [HttpGet("metrics/usage")]
    public async Task<ActionResult<ApiResponse<UsageMetricsResponse>>> GetUsage(
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-days);
        var tenantId = _options.DefaultTenantId;
        var (messages, tokens, cost, avgLatency, avgRetrieval, avgLlm) =
            await _metrics.GetUsageAsync(tenantId, from, to, cancellationToken);
        var feedbackRate = await _metrics.GetPositiveFeedbackRateAsync(tenantId, from, to, cancellationToken);

        return Ok(new ApiResponse<UsageMetricsResponse>(
            new UsageMetricsResponse(messages, tokens, cost, avgLatency, avgRetrieval, avgLlm, feedbackRate, from, to),
            HttpContext.TraceIdentifier));
    }

    [HttpPost("evaluation/trigger")]
    public async Task<ActionResult<ApiResponse<Guid>>> TriggerEvaluation(
        [FromBody] TriggerEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var runId = await _evaluation.TriggerEvaluationAsync(request.DatasetPath, cancellationToken);
        return Accepted(new ApiResponse<Guid>(runId, HttpContext.TraceIdentifier));
    }

    [HttpGet("evaluation/{id:guid}")]
    public async Task<ActionResult<ApiResponse<EvaluationRunDto>>> GetEvaluation(
        Guid id,
        CancellationToken cancellationToken)
    {
        var run = await _evaluation.GetEvaluationStatusAsync(id, cancellationToken);
        if (run == null) return NotFound(new ErrorResponse("Evaluation run not found"));
        return Ok(new ApiResponse<EvaluationRunDto>(run, HttpContext.TraceIdentifier));
    }

    [HttpGet("settings")]
    public async Task<ActionResult<ApiResponse<TenantSettingsDto>>> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await _tenantSettings.GetByTenantAsync(_options.DefaultTenantId, cancellationToken)
            ?? new TenantSettings { TenantId = _options.DefaultTenantId };
        return Ok(new ApiResponse<TenantSettingsDto>(MapSettings(settings), HttpContext.TraceIdentifier));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<ApiResponse<TenantSettingsDto>>> UpdateSettings(
        [FromBody] UpdateTenantSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await _tenantSettings.GetByTenantAsync(_options.DefaultTenantId, cancellationToken)
            ?? new TenantSettings { TenantId = _options.DefaultTenantId };

        if (Enum.TryParse<ChunkingStrategy>(request.DefaultChunkingStrategy, true, out var strategy))
            settings.DefaultChunkingStrategy = strategy;
        settings.FixedChunkSize = request.FixedChunkSize;
        settings.FixedChunkOverlap = request.FixedChunkOverlap;
        settings.RetrievalTopK = request.RetrievalTopK;
        settings.ChatModel = request.ChatModel;
        settings.UseMultiAgent = request.UseMultiAgent;

        if (settings.Id == Guid.Empty)
            await _tenantSettings.AddAsync(settings, cancellationToken);
        else
            await _tenantSettings.UpdateAsync(settings, cancellationToken);

        await _tenantSettings.SaveChangesAsync(cancellationToken);
        return Ok(new ApiResponse<TenantSettingsDto>(MapSettings(settings), HttpContext.TraceIdentifier));
    }

    [HttpPost("documents/{id:guid}/reindex")]
    public async Task<IActionResult> Reindex(Guid id, CancellationToken cancellationToken)
    {
        var document = await _documents.GetByIdAsync(id, cancellationToken);
        if (document == null) return NotFound(new ErrorResponse("Document not found"));

        document.Status = DocumentStatus.Pending;
        document.ErrorMessage = null;
        await _documents.UpdateAsync(document, cancellationToken);
        await _documents.SaveChangesAsync(cancellationToken);

        await _publisher.PublishDocumentUploadedAsync(new Contracts.Ingestion.DocumentUploadedEvent(
            document.Id, document.TenantId, document.BlobPath, document.ContentHash,
            document.ChunkingStrategy.ToString()), cancellationToken);

        return Accepted(new { documentId = id, status = "reindex_queued" });
    }

    private static TenantSettingsDto MapSettings(TenantSettings s) => new(
        s.TenantId, s.DefaultChunkingStrategy.ToString(), s.FixedChunkSize, s.FixedChunkOverlap,
        s.RetrievalTopK, s.ChatModel, s.EmbeddingModel, s.UseMultiAgent, s.DailyTokenBudget);
}
