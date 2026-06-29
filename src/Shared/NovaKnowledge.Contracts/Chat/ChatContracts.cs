namespace NovaKnowledge.Contracts.Chat;

public record CreateSessionRequest(string? UserId = null, string TenantId = "contoso");

public record SessionResponse(Guid Id, string TenantId, string? Title, DateTimeOffset CreatedAt);

public record SendMessageRequest(string Content, string? UserId = null);

public record CitationDto(
    Guid DocumentId,
    int ChunkIndex,
    string Content,
    string? Title,
    double Score);

public record MessageResponse(
    Guid Id,
    string Role,
    string Content,
    IReadOnlyList<CitationDto>? Citations,
    int PromptTokens,
    int CompletionTokens,
    int TotalLatencyMs,
    DateTimeOffset CreatedAt);

public record AgentTraceStep(
    string AgentName,
    string Input,
    string Output,
    int DurationMs,
    int OrderIndex);

public record AgentTraceResponse(
    Guid ConversationId,
    Guid? MessageId,
    IReadOnlyList<AgentTraceStep> Steps);

public record ChatStreamChunk(
    string Type,
    string? Content = null,
    IReadOnlyList<CitationDto>? Citations = null,
    MessageResponse? Message = null);
