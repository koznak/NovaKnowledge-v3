namespace NovaKnowledge.Contracts.Admin;

public record PromptTemplateDto(
    Guid Id,
    string Name,
    string Category,
    int Version,
    string SystemPrompt,
    string UserPromptTemplate,
    bool IsActive,
    string? AbTestTag);

public record CreatePromptTemplateRequest(
    string Name,
    string Category,
    string SystemPrompt,
    string UserPromptTemplate,
    string? AbTestTag);

public record UpdatePromptTemplateRequest(
    string SystemPrompt,
    string UserPromptTemplate,
    bool IsActive);

public record UsageMetricsResponse(
    long TotalMessages,
    long TotalTokens,
    decimal EstimatedCostUsd,
    double AvgLatencyMs,
    double AvgRetrievalLatencyMs,
    double AvgLlmLatencyMs,
    double PositiveFeedbackRate,
    DateTimeOffset From,
    DateTimeOffset To);

public record TriggerEvaluationRequest(string? DatasetPath = null);

public record EvaluationRunDto(
    Guid Id,
    string Status,
    double? FaithfulnessScore,
    double? AnswerRelevanceScore,
    double? ContextPrecisionScore,
    double? ContextRecallScore,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage);

public record TenantSettingsDto(
    string TenantId,
    string DefaultChunkingStrategy,
    int FixedChunkSize,
    int FixedChunkOverlap,
    int RetrievalTopK,
    string ChatModel,
    string EmbeddingModel,
    bool UseMultiAgent,
    decimal DailyTokenBudget);

public record UpdateTenantSettingsRequest(
    string DefaultChunkingStrategy,
    int FixedChunkSize,
    int FixedChunkOverlap,
    int RetrievalTopK,
    string ChatModel,
    bool UseMultiAgent);
