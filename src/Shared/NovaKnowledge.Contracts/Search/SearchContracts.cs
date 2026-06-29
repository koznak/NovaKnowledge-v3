namespace NovaKnowledge.Contracts.Search;

public record SearchQueryRequest(
    string Query,
    string TenantId = "contoso",
    int TopK = 5,
    string? Department = null,
    string? DocType = null,
    bool UseHybrid = true,
    bool UseSemanticRanker = true);

public record SearchResultItem(
    string Id,
    Guid DocumentId,
    int ChunkIndex,
    string Content,
    string? Title,
    string? Department,
    string? DocType,
    double Score,
    string? SourceUrl);

public record SearchQueryResponse(
    IReadOnlyList<SearchResultItem> Results,
    int RetrievalLatencyMs);

public record SearchIndexDocument(
    string Id,
    Guid DocumentId,
    int ChunkIndex,
    string Content,
    IReadOnlyList<float> ContentVector,
    string? Title,
    string? Department,
    string? DocType,
    DateOnly? EffectiveDate,
    string TenantId);
