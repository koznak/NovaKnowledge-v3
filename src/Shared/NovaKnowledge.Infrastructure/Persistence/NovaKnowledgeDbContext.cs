using Microsoft.EntityFrameworkCore;
using NovaKnowledge.Domain.Entities;

namespace NovaKnowledge.Infrastructure.Persistence;

public class NovaKnowledgeDbContext : DbContext
{
    public NovaKnowledgeDbContext(DbContextOptions<NovaKnowledgeDbContext> options) : base(options) { }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<EvaluationRun> EvaluationRuns => Set<EvaluationRun>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.ContentHash }).IsUnique();
            e.Property(x => x.FileName).HasMaxLength(512);
            e.Property(x => x.TenantId).HasMaxLength(64);
        });

        modelBuilder.Entity<DocumentChunk>(e =>
        {
            e.HasOne(x => x.Document).WithMany(x => x.Chunks).HasForeignKey(x => x.DocumentId);
            e.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique();
        });

        modelBuilder.Entity<Conversation>(e =>
        {
            e.Property(x => x.TenantId).HasMaxLength(64);
        });

        modelBuilder.Entity<Message>(e =>
        {
            e.HasOne(x => x.Conversation).WithMany(x => x.Messages).HasForeignKey(x => x.ConversationId);
            e.Property(x => x.EstimatedCostUsd).HasPrecision(10, 6);
        });

        modelBuilder.Entity<AgentRun>(e =>
        {
            e.HasOne(x => x.Conversation).WithMany(x => x.AgentRuns).HasForeignKey(x => x.ConversationId);
            e.Property(x => x.AgentName).HasMaxLength(128);
        });

        modelBuilder.Entity<Feedback>(e =>
        {
            e.Property(x => x.TenantId).HasMaxLength(64);
        });

        modelBuilder.Entity<PromptTemplate>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name, x.Version }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Category).HasMaxLength(64);
        });

        modelBuilder.Entity<TenantSettings>(e =>
        {
            e.HasIndex(x => x.TenantId).IsUnique();
            e.Property(x => x.DailyTokenBudget).HasPrecision(12, 2);
        });

        modelBuilder.Entity<EvaluationRun>(e =>
        {
            e.Property(x => x.FaithfulnessScore).HasPrecision(5, 4);
            e.Property(x => x.AnswerRelevanceScore).HasPrecision(5, 4);
            e.Property(x => x.ContextPrecisionScore).HasPrecision(5, 4);
            e.Property(x => x.ContextRecallScore).HasPrecision(5, 4);
        });
    }
}
