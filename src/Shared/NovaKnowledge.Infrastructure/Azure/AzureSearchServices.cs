using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Search;
using NovaKnowledge.Infrastructure.Configuration;

namespace NovaKnowledge.Infrastructure.Azure;

/// <summary>Helpers for building safe Azure AI Search OData filter expressions.</summary>
internal static class SearchFilter
{
    /// <summary>Escapes a single-quoted OData string literal by doubling embedded quotes.</summary>
    public static string Literal(string value) => value.Replace("'", "''");

    public static string Eq(string field, string value) => $"{field} eq '{Literal(value)}'";
}

public class AzureSearchIndexService : ISearchIndexService
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly AzureSearchOptions _options;
    private readonly ILogger<AzureSearchIndexService> _logger;

    public AzureSearchIndexService(
        SearchIndexClient indexClient,
        SearchClient searchClient,
        IOptions<AzureSearchOptions> options,
        ILogger<AzureSearchIndexService> logger)
    {
        _indexClient = indexClient;
        _searchClient = searchClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureIndexExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _indexClient.GetIndexAsync(_options.IndexName, cancellationToken);
            return;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Creating search index {IndexName}", _options.IndexName);
        }

        var fields = new List<SearchField>
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new SearchableField("content") { IsFilterable = false },
            new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = _options.VectorDimensions,
                VectorSearchProfileName = "vector-profile"
            },
            new SimpleField("documentId", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SimpleField("chunkIndex", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
            new SearchableField("title") { IsFilterable = true, IsFacetable = true },
            new SimpleField("department", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SimpleField("docType", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SimpleField("effectiveDate", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
            new SimpleField("tenantId", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true }
        };

        var index = new SearchIndex(_options.IndexName)
        {
            Fields = fields,
            VectorSearch = new VectorSearch
            {
                Profiles =
                {
                    new VectorSearchProfile("vector-profile", "hnsw-config")
                },
                Algorithms =
                {
                    new HnswAlgorithmConfiguration("hnsw-config")
                }
            },
            SemanticSearch = new SemanticSearch
            {
                Configurations =
                {
                    new SemanticConfiguration("semantic-config", new SemanticPrioritizedFields
                    {
                        TitleField = new SemanticField("title"),
                        ContentFields = { new SemanticField("content") }
                    })
                }
            }
        };

        await _indexClient.CreateIndexAsync(index, cancellationToken);
    }

    public async Task IndexDocumentsAsync(IReadOnlyList<SearchIndexDocument> documents, CancellationToken cancellationToken = default)
    {
        var batch = IndexDocumentsBatch.MergeOrUpload(documents.Select(d => new SearchDocument
        {
            ["id"] = d.Id,
            ["documentId"] = d.DocumentId.ToString(),
            ["chunkIndex"] = d.ChunkIndex,
            ["content"] = d.Content,
            ["contentVector"] = d.ContentVector,
            ["title"] = d.Title,
            ["department"] = d.Department,
            ["docType"] = d.DocType,
            ["effectiveDate"] = d.EffectiveDate?.ToDateTime(TimeOnly.MinValue),
            ["tenantId"] = d.TenantId
        }));

        await _searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);
        _logger.LogInformation("Indexed {Count} documents", documents.Count);
    }

    public async Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var filter = SearchFilter.Eq("documentId", documentId.ToString());
        var results = await _searchClient.SearchAsync<SearchDocument>("*", new SearchOptions { Filter = filter, Select = { "id" } }, cancellationToken);
        var ids = new List<string>();
        await foreach (var result in results.Value.GetResultsAsync())
        {
            if (result.Document.TryGetValue("id", out var id))
                ids.Add(id.ToString()!);
        }

        if (ids.Count == 0) return;

        var batch = IndexDocumentsBatch.Delete(ids.Select(id => new SearchDocument { ["id"] = id }));
        await _searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);
    }
}

public class AzureSearchQueryService : ISearchQueryService
{
    private readonly SearchClient _searchClient;
    private readonly IEmbeddingService _embeddingService;
    private readonly AzureSearchOptions _searchOptions;
    private readonly ILogger<AzureSearchQueryService> _logger;

    public AzureSearchQueryService(
        SearchClient searchClient,
        IEmbeddingService embeddingService,
        IOptions<AzureSearchOptions> searchOptions,
        ILogger<AzureSearchQueryService> logger)
    {
        _searchClient = searchClient;
        _embeddingService = embeddingService;
        _searchOptions = searchOptions.Value;
        _logger = logger;
    }

    public async Task<SearchQueryResponse> SearchAsync(SearchQueryRequest request, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var filters = new List<string> { SearchFilter.Eq("tenantId", request.TenantId) };
        if (!string.IsNullOrEmpty(request.Department))
            filters.Add(SearchFilter.Eq("department", request.Department));
        if (!string.IsNullOrEmpty(request.DocType))
            filters.Add(SearchFilter.Eq("docType", request.DocType));

        var options = new SearchOptions
        {
            Size = request.TopK,
            Filter = string.Join(" and ", filters),
            Select = { "id", "documentId", "chunkIndex", "content", "title", "department", "docType" }
        };

        if (request.UseHybrid && _searchOptions.EnableHybridSearch)
        {
            var vector = await _embeddingService.GenerateEmbeddingAsync(request.Query, cancellationToken);
            options.VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(vector) { KNearestNeighborsCount = request.TopK, Fields = { "contentVector" } }
                }
            };
        }

        if (request.UseSemanticRanker)
        {
            options.QueryType = SearchQueryType.Semantic;
            options.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = "semantic-config",
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                QueryAnswer = new QueryAnswer(QueryAnswerType.Extractive)
            };
        }

        var response = await _searchClient.SearchAsync<SearchDocument>(request.Query, options, cancellationToken);
        var results = new List<SearchResultItem>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;
            results.Add(new SearchResultItem(
                doc.GetString("id") ?? string.Empty,
                Guid.Parse(doc.GetString("documentId") ?? Guid.Empty.ToString()),
                doc.GetInt32("chunkIndex") ?? 0,
                doc.GetString("content") ?? string.Empty,
                doc.GetString("title"),
                doc.GetString("department"),
                doc.GetString("docType"),
                result.Score ?? 0,
                null));
        }

        sw.Stop();
        _logger.LogDebug("Search returned {Count} results in {Ms}ms", results.Count, sw.ElapsedMilliseconds);
        return new SearchQueryResponse(results, (int)sw.ElapsedMilliseconds);
    }
}
