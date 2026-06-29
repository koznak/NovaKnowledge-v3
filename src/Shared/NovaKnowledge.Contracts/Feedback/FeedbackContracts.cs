namespace NovaKnowledge.Contracts.Feedback;

public record SubmitFeedbackRequest(
    Guid MessageId,
    int Rating,
    string? Comment = null,
    string? UserId = null,
    string TenantId = "contoso");

public record FeedbackResponse(
    Guid Id,
    Guid MessageId,
    int Rating,
    string? Comment,
    DateTimeOffset CreatedAt);

public record FeedbackExportRecord(
    Guid MessageId,
    int Rating,
    string? Comment,
    string TenantId,
    DateTimeOffset CreatedAt);
