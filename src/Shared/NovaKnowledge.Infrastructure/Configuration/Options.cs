namespace NovaKnowledge.Infrastructure.Configuration;

public class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>Optional separate resource for embeddings (falls back to Endpoint when unset).</summary>
    public string? EmbeddingEndpoint { get; set; }
    /// <summary>Optional separate API key for embeddings (falls back to ApiKey when unset).</summary>
    public string? EmbeddingApiKey { get; set; }
    public string ChatDeployment { get; set; } = "gpt-5-mini-2";
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-large";
    public int EmbeddingDimensions { get; set; } = 3072;

    public string ResolveEmbeddingEndpoint() =>
        string.IsNullOrWhiteSpace(EmbeddingEndpoint) ? Endpoint : EmbeddingEndpoint;

    public string ResolveEmbeddingApiKey() =>
        string.IsNullOrWhiteSpace(EmbeddingApiKey) ? ApiKey : EmbeddingApiKey;
}

public class AzureSearchOptions
{
    public const string SectionName = "AzureSearch";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string IndexName { get; set; } = "novaknowledge-documents";
    /// <summary>When false, search uses Azure AI Search keyword + semantic ranker only (no Azure OpenAI embeddings).</summary>
    public bool EnableHybridSearch { get; set; } = true;
    /// <summary>Vector field dimensionality; must match the embedding model (3072 for text-embedding-3-large, 1536 for small/ada-002).</summary>
    public int VectorDimensions { get; set; } = 3072;
}

public class AzureStorageOptions
{
    public const string SectionName = "AzureStorage";
    public string ConnectionString { get; set; } = string.Empty;
    public string DocumentsContainer { get; set; } = "documents";
    public string ExportsContainer { get; set; } = "exports";
}

public class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";
    public string ConnectionString { get; set; } = string.Empty;
    public string DocumentUploadedQueue { get; set; } = "document-uploaded";
}

public class RedisOptions
{
    public const string SectionName = "Redis";
    public string ConnectionString { get; set; } = string.Empty;
}

public class ServiceUrlsOptions
{
    public const string SectionName = "ServiceUrls";
    public string SearchApi { get; set; } = "http://localhost:5003";
    public string OrchestrationApi { get; set; } = "http://localhost:5002";
    public string EvaluationService { get; set; } = "http://localhost:8000";
}

public class NovaKnowledgeOptions
{
    public const string SectionName = "NovaKnowledge";
    public string DefaultTenantId { get; set; } = "contoso";
}

/// <summary>
/// Approximate per-token pricing used to estimate request cost for the metrics dashboard.
/// Values are placeholders and should be updated from the current Azure OpenAI price list.
/// </summary>
public class PricingOptions
{
    public const string SectionName = "Pricing";
    /// <summary>USD per 1,000 prompt (input) tokens.</summary>
    public decimal PromptCostPer1KTokens { get; set; } = 0.00015m;
    /// <summary>USD per 1,000 completion (output) tokens.</summary>
    public decimal CompletionCostPer1KTokens { get; set; } = 0.0006m;
}
