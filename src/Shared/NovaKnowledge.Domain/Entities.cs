using NovaKnowledge.Domain.Entities;

namespace NovaKnowledge.Domain.Entities;

public class Document : Entity
{
    public string TenantId { get; set; } = "contoso";
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public string? Title { get; set; }
    public string? Department { get; set; }
    public string? DocType { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public string? ErrorMessage { get; set; }
    public ChunkingStrategy ChunkingStrategy { get; set; } = ChunkingStrategy.Recursive;
    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
}

public class DocumentChunk : Entity
{
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? SearchIndexKey { get; set; }
    public int TokenCount { get; set; }
    public ChunkingStrategy StrategyUsed { get; set; }
    public string EmbeddingModel { get; set; } = "text-embedding-3-large";
}

public class Conversation : Entity
{
    public string TenantId { get; set; } = "contoso";
    public string? UserId { get; set; }
    public string? Title { get; set; }
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<AgentRun> AgentRuns { get; set; } = new List<AgentRun>();
}

public class Message : Entity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? CitationsJson { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int RetrievalLatencyMs { get; set; }
    public int LlmLatencyMs { get; set; }
    public int TotalLatencyMs { get; set; }
    public decimal EstimatedCostUsd { get; set; }
}

public class AgentRun : Entity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public Guid? MessageId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public int OrderIndex { get; set; }
}

public class Feedback : Entity
{
    public Guid MessageId { get; set; }
    public string TenantId { get; set; } = "contoso";
    public FeedbackRating Rating { get; set; }
    public string? Comment { get; set; }
    public string? UserId { get; set; }
}

public class PromptTemplate : Entity
{
    public string TenantId { get; set; } = "contoso";
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "general";
    public int Version { get; set; } = 1;
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPromptTemplate { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? AbTestTag { get; set; }
}

public class EvaluationRun : Entity
{
    public string TenantId { get; set; } = "contoso";
    public EvaluationStatus Status { get; set; } = EvaluationStatus.Pending;
    public string? DatasetPath { get; set; }
    public double? FaithfulnessScore { get; set; }
    public double? AnswerRelevanceScore { get; set; }
    public double? ContextPrecisionScore { get; set; }
    public double? ContextRecallScore { get; set; }
    public string? ReportBlobPath { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class TenantSettings : Entity
{
    public string TenantId { get; set; } = "contoso";
    public ChunkingStrategy DefaultChunkingStrategy { get; set; } = ChunkingStrategy.Recursive;
    public int FixedChunkSize { get; set; } = 512;
    public int FixedChunkOverlap { get; set; } = 128;
    public int RetrievalTopK { get; set; } = 5;
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public string EmbeddingModel { get; set; } = "text-embedding-3-large";
    public bool UseMultiAgent { get; set; } = true;
    public decimal DailyTokenBudget { get; set; } = 1_000_000;
}
