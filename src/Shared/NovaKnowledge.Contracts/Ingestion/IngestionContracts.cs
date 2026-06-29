namespace NovaKnowledge.Contracts.Ingestion;

public record UploadDocumentRequest(
    string? Title,
    string? Department,
    string? DocType,
    DateOnly? EffectiveDate,
    string ChunkingStrategy = "Recursive");

public record DocumentResponse(
    Guid Id,
    string FileName,
    string Status,
    string? Title,
    string? Department,
    string? DocType,
    DateOnly? EffectiveDate,
    string ChunkingStrategy,
    DateTimeOffset CreatedAt,
    string? ErrorMessage);

public record DocumentStatusResponse(
    Guid Id,
    string Status,
    int ChunkCount,
    string? ErrorMessage);

public record DocumentUploadedEvent(
    Guid DocumentId,
    string TenantId,
    string BlobPath,
    string ContentHash,
    string ChunkingStrategy);
