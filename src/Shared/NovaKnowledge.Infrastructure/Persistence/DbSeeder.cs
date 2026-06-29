using Microsoft.EntityFrameworkCore;
using NovaKnowledge.Domain.Entities;
using NovaKnowledge.Infrastructure.Persistence;

namespace NovaKnowledge.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(NovaKnowledgeDbContext db)
    {
        if (!await db.PromptTemplates.AnyAsync())
        {
            db.PromptTemplates.AddRange(
                new PromptTemplate
                {
                    TenantId = "contoso",
                    Name = "General Knowledge",
                    Category = "general",
                    Version = 1,
                    IsActive = true,
                    SystemPrompt = """
                        You are NovaKnowledge, Contoso Corp's enterprise knowledge copilot.
                        Answer using only the provided context. Cite sources with [n] notation.
                        If information is insufficient, say so clearly.
                        """,
                    UserPromptTemplate = "Context:\n{context}\n\nQuestion: {question}"
                },
                new PromptTemplate
                {
                    TenantId = "contoso",
                    Name = "HR Policy",
                    Category = "hr",
                    Version = 1,
                    IsActive = true,
                    SystemPrompt = """
                        You are an HR policy assistant for Contoso Corp.
                        Provide accurate, citation-backed answers about HR policies.
                        Always recommend contacting HR for final decisions on sensitive matters.
                        """,
                    UserPromptTemplate = "HR Context:\n{context}\n\nEmployee Question: {question}"
                },
                new PromptTemplate
                {
                    TenantId = "contoso",
                    Name = "IT Support",
                    Category = "it",
                    Version = 1,
                    IsActive = true,
                    SystemPrompt = """
                        You are an IT support assistant for Contoso Corp.
                        Help employees with IT policies and troubleshooting guidance from official docs.
                        """,
                    UserPromptTemplate = "IT Context:\n{context}\n\nQuestion: {question}"
                });
        }

        if (!await db.TenantSettings.AnyAsync())
        {
            db.TenantSettings.Add(new TenantSettings
            {
                TenantId = "contoso",
                DefaultChunkingStrategy = ChunkingStrategy.Recursive,
                FixedChunkSize = 512,
                FixedChunkOverlap = 128,
                RetrievalTopK = 5,
                ChatModel = "gpt-5-mini-2",
                EmbeddingModel = "text-embedding-3-large",
                UseMultiAgent = false,
                DailyTokenBudget = 1_000_000
            });
        }

        var contosoSettings = await db.TenantSettings.FirstOrDefaultAsync(t => t.TenantId == "contoso");
        if (contosoSettings != null)
        {
            contosoSettings.UseMultiAgent = false;
            if (contosoSettings.ChatModel is "gpt-4o-mini" or "gpt-5-mini")
                contosoSettings.ChatModel = "gpt-5-mini-2";
        }

        await db.SaveChangesAsync();
    }
}
