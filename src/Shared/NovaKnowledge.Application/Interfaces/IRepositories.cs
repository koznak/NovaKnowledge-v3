using NovaKnowledge.Domain.Entities;

namespace NovaKnowledge.Application.Interfaces;

public interface IRepository<T> where T : Entity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IDocumentRepository : IRepository<Document>
{
    Task<Document?> GetByContentHashAsync(string hash, string tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Document>> ListAsync(string tenantId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Document?> GetByIdUntrackedAsync(Guid id, CancellationToken cancellationToken = default);
    Task SetStatusAsync(Guid id, DocumentStatus status, string? errorMessage, CancellationToken cancellationToken = default);
    Task AddChunksAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default);
    Task DeleteChunksAsync(Guid documentId, CancellationToken cancellationToken = default);
}

public interface IConversationRepository : IRepository<Conversation>
{
    Task<Conversation?> GetWithMessagesAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(Guid Id, string TenantId)?> GetSessionContextAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Message> AddMessageAsync(Guid conversationId, Message message, CancellationToken cancellationToken = default);
    Task AddAgentRunAsync(AgentRun agentRun, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentRun>> GetAgentRunsAsync(Guid conversationId, Guid? messageId, CancellationToken cancellationToken = default);
}

public interface IFeedbackRepository : IRepository<Feedback>
{
    Task<IReadOnlyList<Feedback>> GetForExportAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}

public interface IPromptTemplateRepository : IRepository<PromptTemplate>
{
    Task<IReadOnlyList<PromptTemplate>> ListAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<PromptTemplate?> GetActiveAsync(string category, string tenantId, CancellationToken cancellationToken = default);
}

public interface IEvaluationRunRepository : IRepository<EvaluationRun>
{
    Task<IReadOnlyList<EvaluationRun>> ListRecentAsync(string tenantId, int count, CancellationToken cancellationToken = default);
}

public interface ITenantSettingsRepository : IRepository<TenantSettings>
{
    Task<TenantSettings?> GetByTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}

public interface IUsageMetricsRepository
{
    Task<(long Messages, long Tokens, decimal Cost, double AvgLatency, double AvgRetrieval, double AvgLlm)> GetUsageAsync(
        string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<double> GetPositiveFeedbackRateAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
