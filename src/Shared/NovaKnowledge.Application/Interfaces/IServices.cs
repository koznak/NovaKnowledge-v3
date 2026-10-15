namespace NovaKnowledge.Application.Interfaces;

using NovaKnowledge.Contracts.Admin;
using NovaKnowledge.Contracts.Chat;
using NovaKnowledge.Contracts.Ingestion;
using NovaKnowledge.Contracts.Search;

public interface IChunkingService
{
    IReadOnlyList<string> ChunkText(string text, string strategy, int chunkSize = 512, int overlap = 128);
}

public interface ITextExtractionService
{
    Task<string> ExtractTextAsync(Stream stream, string contentType, CancellationToken cancellationToken = default);
}

public interface IEmbeddingService
{
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}

public interface ISearchIndexService
{
    Task EnsureIndexExistsAsync(CancellationToken cancellationToken = default);
    Task IndexDocumentsAsync(IReadOnlyList<SearchIndexDocument> documents, CancellationToken cancellationToken = default);
    Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);
}

public interface ISearchQueryService
{
    Task<SearchQueryResponse> SearchAsync(SearchQueryRequest request, CancellationToken cancellationToken = default);
}

public interface IChatCompletionService
{
    Task<(string Content, int PromptTokens, int CompletionTokens)> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatStreamUpdate> StreamAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}

/// <summary>A single update from a streaming chat completion.</summary>
/// <param name="TokenDelta">Incremental token text emitted by the model, if any.</param>
/// <param name="PromptTokens">Prompt token count, available on the final update.</param>
/// <param name="CompletionTokens">Completion token count, available on the final update.</param>
/// <param name="IsFinal">True for the terminal update carrying usage.</param>
public record ChatStreamUpdate(
    string? TokenDelta,
    int? PromptTokens,
    int? CompletionTokens,
    bool IsFinal);

public interface IBlobStorageService
{
    Task<string> UploadAsync(string container, string blobName, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string container, string blobPath, CancellationToken cancellationToken = default);
    Task<string> UploadJsonlAsync(string container, string blobName, IEnumerable<string> lines, CancellationToken cancellationToken = default);
}

public interface IMessagePublisher
{
    Task PublishDocumentUploadedAsync(DocumentUploadedEvent message, CancellationToken cancellationToken = default);
}

public interface IAgentOrchestrator
{
    Task<AgentOrchestrationResult> OrchestrateAsync(AgentOrchestrationRequest request, CancellationToken cancellationToken = default);
}

public record AgentOrchestrationRequest(
    string UserMessage,
    string TenantId,
    Guid ConversationId,
    IReadOnlyList<SearchResultItem> RetrievedChunks,
    string SystemPrompt);

public record AgentOrchestrationResult(
    string FinalAnswer,
    IReadOnlyList<AgentTraceStep> Trace,
    int TotalLlmLatencyMs,
    int PromptTokens,
    int CompletionTokens);

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
}

public interface IPromptTemplateService
{
    Task<Domain.Entities.PromptTemplate?> GetActiveTemplateAsync(string category, string tenantId, CancellationToken cancellationToken = default);
}

public interface IEvaluationClient
{
    Task<Guid> TriggerEvaluationAsync(string? datasetPath, CancellationToken cancellationToken = default);
    Task<EvaluationRunDto?> GetEvaluationStatusAsync(Guid runId, CancellationToken cancellationToken = default);
}
