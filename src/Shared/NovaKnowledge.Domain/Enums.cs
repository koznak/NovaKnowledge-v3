namespace NovaKnowledge.Domain.Entities;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum DocumentStatus
{
    Pending = 0,
    Processing = 1,
    Indexed = 2,
    Failed = 3
}

public enum ChunkingStrategy
{
    Recursive = 0,
    Fixed = 1,
    Semantic = 2
}

public enum MessageRole
{
    User = 0,
    Assistant = 1,
    System = 2
}

public enum FeedbackRating
{
    Negative = -1,
    Neutral = 0,
    Positive = 1
}

public enum EvaluationStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}
