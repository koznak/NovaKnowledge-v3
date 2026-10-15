using Microsoft.EntityFrameworkCore;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Domain.Entities;
using NovaKnowledge.Infrastructure.Persistence;

namespace NovaKnowledge.Infrastructure.Repositories;

public class EfRepository<T>(NovaKnowledgeDbContext db) : IRepository<T> where T : Entity
{
    protected NovaKnowledgeDbContext Db => db;
    protected DbSet<T> Set => db.Set<T>();

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Set.FindAsync([id], cancellationToken);

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        entity.CreatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await Set.AddAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        Set.Update(entity);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await db.SaveChangesAsync(cancellationToken);
}

public class DocumentRepository(NovaKnowledgeDbContext db) : EfRepository<Document>(db), IDocumentRepository
{
    public async Task<Document?> GetByContentHashAsync(string hash, string tenantId, CancellationToken cancellationToken = default) =>
        await Db.Documents.FirstOrDefaultAsync(x => x.ContentHash == hash && x.TenantId == tenantId, cancellationToken);

    public async Task<IReadOnlyList<Document>> ListAsync(string tenantId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        await Db.Documents
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public Task<Document?> GetByIdUntrackedAsync(Guid id, CancellationToken cancellationToken = default) =>
        Db.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task SetStatusAsync(Guid id, DocumentStatus status, string? errorMessage, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await Db.Documents
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.ErrorMessage, errorMessage)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);
    }

    public async Task AddChunksAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var chunk in chunks)
        {
            chunk.CreatedAt = now;
            chunk.UpdatedAt = now;
        }

        await Db.DocumentChunks.AddRangeAsync(chunks, cancellationToken);
        await Db.SaveChangesAsync(cancellationToken);
    }

    public Task DeleteChunksAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Db.DocumentChunks.Where(x => x.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);
}

public class ConversationRepository(NovaKnowledgeDbContext db) : EfRepository<Conversation>(db), IConversationRepository
{
    public async Task<Conversation?> GetWithMessagesAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Db.Conversations
            .AsNoTracking()
            .Include(x => x.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<(Guid Id, string TenantId)?> GetSessionContextAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await Db.Conversations
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.TenantId })
            .FirstOrDefaultAsync(cancellationToken);

        return session == null ? null : (session.Id, session.TenantId);
    }

    public async Task<Message> AddMessageAsync(Guid conversationId, Message message, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        message.ConversationId = conversationId;
        message.CreatedAt = now;
        message.UpdatedAt = now;

        await Db.Messages.AddAsync(message, cancellationToken);
        await Db.Conversations
            .Where(x => x.Id == conversationId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, now), cancellationToken);

        return message;
    }

    public async Task AddAgentRunAsync(AgentRun agentRun, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        agentRun.CreatedAt = now;
        agentRun.UpdatedAt = now;
        await Db.AgentRuns.AddAsync(agentRun, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentRun>> GetAgentRunsAsync(Guid conversationId, Guid? messageId, CancellationToken cancellationToken = default)
    {
        var query = Db.AgentRuns.Where(x => x.ConversationId == conversationId);
        if (messageId.HasValue)
            query = query.Where(x => x.MessageId == messageId);

        return await query.OrderBy(x => x.OrderIndex).ToListAsync(cancellationToken);
    }
}

public class FeedbackRepository(NovaKnowledgeDbContext db) : EfRepository<Feedback>(db), IFeedbackRepository
{
    public async Task<IReadOnlyList<Feedback>> GetForExportAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        await Db.Feedbacks.Where(x => x.CreatedAt >= since).OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
}

public class PromptTemplateRepository(NovaKnowledgeDbContext db) : EfRepository<PromptTemplate>(db), IPromptTemplateRepository
{
    public async Task<IReadOnlyList<PromptTemplate>> ListAsync(string tenantId, CancellationToken cancellationToken = default) =>
        await Db.PromptTemplates.Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ThenByDescending(x => x.Version).ToListAsync(cancellationToken);

    public async Task<PromptTemplate?> GetActiveAsync(string category, string tenantId, CancellationToken cancellationToken = default) =>
        await Db.PromptTemplates
            .Where(x => x.TenantId == tenantId && x.Category == category && x.IsActive)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);
}

public class EvaluationRunRepository(NovaKnowledgeDbContext db) : EfRepository<EvaluationRun>(db), IEvaluationRunRepository
{
    public async Task<IReadOnlyList<EvaluationRun>> ListRecentAsync(string tenantId, int count, CancellationToken cancellationToken = default) =>
        await Db.EvaluationRuns
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
}

public class TenantSettingsRepository(NovaKnowledgeDbContext db) : EfRepository<TenantSettings>(db), ITenantSettingsRepository
{
    public async Task<TenantSettings?> GetByTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
        await Db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
}

public class UsageMetricsRepository(NovaKnowledgeDbContext db) : IUsageMetricsRepository
{
    public async Task<(long Messages, long Tokens, decimal Cost, double AvgLatency, double AvgRetrieval, double AvgLlm)> GetUsageAsync(
        string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var query = db.Messages
            .Where(m => m.Conversation.TenantId == tenantId && m.CreatedAt >= from && m.CreatedAt <= to);

        if (!await query.AnyAsync(cancellationToken))
            return (0, 0, 0, 0, 0, 0);

        // Aggregate server-side; only scalars cross the wire.
        var messages = await query.CountAsync(cancellationToken);
        var tokens = await query.SumAsync(m => (long)(m.PromptTokens + m.CompletionTokens), cancellationToken);
        var cost = await query.SumAsync(m => m.EstimatedCostUsd, cancellationToken);
        var avgLatency = await query.AverageAsync(m => (double)m.TotalLatencyMs, cancellationToken);
        var avgRetrieval = await query.AverageAsync(m => (double)m.RetrievalLatencyMs, cancellationToken);
        var avgLlm = await query.AverageAsync(m => (double)m.LlmLatencyMs, cancellationToken);

        return (messages, tokens, cost, avgLatency, avgRetrieval, avgLlm);
    }

    public async Task<double> GetPositiveFeedbackRateAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var feedbacks = db.Feedbacks
            .Where(f => f.TenantId == tenantId && f.CreatedAt >= from && f.CreatedAt <= to);

        var total = await feedbacks.CountAsync(cancellationToken);
        if (total == 0) return 0;

        var positive = await feedbacks.CountAsync(f => f.Rating == FeedbackRating.Positive, cancellationToken);
        return positive / (double)total;
    }
}
