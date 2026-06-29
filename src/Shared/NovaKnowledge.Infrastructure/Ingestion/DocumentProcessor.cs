using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Ingestion;
using NovaKnowledge.Contracts.Search;
using NovaKnowledge.Domain.Entities;
using NovaKnowledge.Infrastructure.Configuration;

namespace NovaKnowledge.Infrastructure.Ingestion;

public class DocumentProcessor
{
    private readonly IDocumentRepository _documents;
    private readonly IBlobStorageService _blobStorage;
    private readonly ITextExtractionService _textExtraction;
    private readonly IChunkingService _chunking;
    private readonly IEmbeddingService _embeddings;
    private readonly ISearchIndexService _searchIndex;
    private readonly ITenantSettingsRepository _tenantSettings;
    private readonly AzureStorageOptions _storageOptions;
    private readonly AzureSearchOptions _searchOptions;
    private readonly AzureOpenAIOptions _openAiOptions;
    private readonly ILogger<DocumentProcessor> _logger;

    public DocumentProcessor(
        IDocumentRepository documents,
        IBlobStorageService blobStorage,
        ITextExtractionService textExtraction,
        IChunkingService chunking,
        IEmbeddingService embeddings,
        ISearchIndexService searchIndex,
        ITenantSettingsRepository tenantSettings,
        IOptions<AzureStorageOptions> storageOptions,
        IOptions<AzureSearchOptions> searchOptions,
        IOptions<AzureOpenAIOptions> openAiOptions,
        ILogger<DocumentProcessor> logger)
    {
        _documents = documents;
        _blobStorage = blobStorage;
        _textExtraction = textExtraction;
        _chunking = chunking;
        _embeddings = embeddings;
        _searchIndex = searchIndex;
        _tenantSettings = tenantSettings;
        _storageOptions = storageOptions.Value;
        _searchOptions = searchOptions.Value;
        _openAiOptions = openAiOptions.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(DocumentUploadedEvent message, CancellationToken cancellationToken)
    {
        var document = await _documents.GetByIdUntrackedAsync(message.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document {message.DocumentId} not found");

        await _documents.SetStatusAsync(document.Id, DocumentStatus.Processing, null, cancellationToken);

        try
        {
            await _searchIndex.EnsureIndexExistsAsync(cancellationToken);
            await _searchIndex.DeleteByDocumentIdAsync(document.Id, cancellationToken);
            await _documents.DeleteChunksAsync(document.Id, cancellationToken);

            await using var stream = await _blobStorage.DownloadAsync(_storageOptions.DocumentsContainer, document.BlobPath, cancellationToken);
            var text = await _textExtraction.ExtractTextAsync(stream, document.ContentType, cancellationToken);

            var settings = await _tenantSettings.GetByTenantAsync(document.TenantId, cancellationToken);
            var chunkSize = settings?.FixedChunkSize ?? 512;
            var overlap = settings?.FixedChunkOverlap ?? 128;
            var strategy = document.ChunkingStrategy.ToString();

            var chunks = _chunking.ChunkText(text, strategy, chunkSize, overlap);
            if (chunks.Count == 0)
                throw new InvalidOperationException("No text extracted from document");

            IReadOnlyList<float[]> embeddings;
            if (_searchOptions.EnableHybridSearch)
            {
                embeddings = await _embeddings.GenerateEmbeddingsAsync(chunks, cancellationToken);
            }
            else
            {
                embeddings = chunks.Select(_ => new float[_openAiOptions.EmbeddingDimensions]).ToList();
                _logger.LogInformation("Keyword-only mode: skipping embeddings for document {DocumentId}", document.Id);
            }

            var chunkEntities = new List<DocumentChunk>();
            var indexDocs = new List<SearchIndexDocument>();

            for (var i = 0; i < chunks.Count; i++)
            {
                var searchIndexKey = $"{document.Id}_{i}";
                chunkEntities.Add(new DocumentChunk
                {
                    DocumentId = document.Id,
                    ChunkIndex = i,
                    Content = chunks[i],
                    StrategyUsed = document.ChunkingStrategy,
                    SearchIndexKey = searchIndexKey,
                    TokenCount = chunks[i].Length / 4
                });

                indexDocs.Add(new SearchIndexDocument(
                    searchIndexKey,
                    document.Id,
                    i,
                    chunks[i],
                    embeddings[i],
                    document.Title,
                    document.Department,
                    document.DocType,
                    document.EffectiveDate,
                    document.TenantId));
            }

            const int batchSize = 16;
            for (var i = 0; i < indexDocs.Count; i += batchSize)
            {
                var batch = indexDocs.Skip(i).Take(batchSize).ToList();
                await _searchIndex.IndexDocumentsAsync(batch, cancellationToken);
            }

            await _documents.AddChunksAsync(chunkEntities, cancellationToken);
            await _documents.SetStatusAsync(document.Id, DocumentStatus.Indexed, null, cancellationToken);
            _logger.LogInformation("Indexed document {DocumentId} with {ChunkCount} chunks", document.Id, chunks.Count);
        }
        catch (Exception ex)
        {
            await _documents.SetStatusAsync(document.Id, DocumentStatus.Failed, ex.Message, cancellationToken);
            _logger.LogError(ex, "Document processing failed for {DocumentId}", document.Id);
            throw;
        }
    }
}
