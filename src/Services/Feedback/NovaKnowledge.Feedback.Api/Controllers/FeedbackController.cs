using Microsoft.AspNetCore.Mvc;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Common;
using NovaKnowledge.Contracts.Feedback;
using NovaKnowledge.Domain.Entities;
using NovaKnowledge.Infrastructure.Configuration;

namespace NovaKnowledge.Feedback.Api.Controllers;

[ApiController]
[Route("api/feedback")]
public class FeedbackController : ControllerBase
{
    private readonly IFeedbackRepository _feedback;
    private readonly IBlobStorageService _blobStorage;
    private readonly AzureStorageOptions _storageOptions;

    public FeedbackController(
        IFeedbackRepository feedback,
        IBlobStorageService blobStorage,
        Microsoft.Extensions.Options.IOptions<AzureStorageOptions> storageOptions)
    {
        _feedback = feedback;
        _blobStorage = blobStorage;
        _storageOptions = storageOptions.Value;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<FeedbackResponse>>> Submit(
        [FromBody] SubmitFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var rating = request.Rating switch
        {
            > 0 => FeedbackRating.Positive,
            < 0 => FeedbackRating.Negative,
            _ => FeedbackRating.Neutral
        };

        var entity = new Domain.Entities.Feedback
        {
            MessageId = request.MessageId,
            TenantId = request.TenantId,
            Rating = rating,
            Comment = request.Comment,
            UserId = request.UserId
        };

        await _feedback.AddAsync(entity, cancellationToken);
        await _feedback.SaveChangesAsync(cancellationToken);

        return Created($"/api/feedback/{entity.Id}",
            new ApiResponse<FeedbackResponse>(
                new FeedbackResponse(entity.Id, entity.MessageId, request.Rating, entity.Comment, entity.CreatedAt),
                HttpContext.TraceIdentifier));
    }

    [HttpPost("export")]
    public async Task<ActionResult<ApiResponse<string>>> Export(
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var items = await _feedback.GetForExportAsync(since, cancellationToken);
        var lines = items.Select(f => System.Text.Json.JsonSerializer.Serialize(new FeedbackExportRecord(
            f.MessageId, (int)f.Rating, f.Comment, f.TenantId, f.CreatedAt)));

        var blobName = $"feedback/export-{DateTime.UtcNow:yyyyMMddHHmmss}.jsonl";
        var uri = await _blobStorage.UploadJsonlAsync(_storageOptions.ExportsContainer, blobName, lines, cancellationToken);
        return Ok(new ApiResponse<string>(uri, HttpContext.TraceIdentifier));
    }
}
