namespace NovaKnowledge.Admin.Web.Services;

using NovaKnowledge.Contracts.Admin;
using NovaKnowledge.Contracts.Common;

public class AdminApiClient(HttpClient http)
{
    private readonly HttpClient _http = http;

    public async Task<UsageMetrics?> GetUsageAsync(int days = 7)
    {
        var response = await _http.GetFromJsonAsync<ApiResponse<UsageMetrics>>($"/api/admin/metrics/usage?days={days}");
        return response?.Data;
    }

    public async Task<IReadOnlyList<PromptTemplate>?> GetPromptsAsync()
    {
        var response = await _http.GetFromJsonAsync<ApiResponse<List<PromptTemplate>>>("/api/admin/prompts");
        return response?.Data;
    }

    public async Task<IReadOnlyList<DocumentItem>?> GetDocumentsAsync()
    {
        var response = await _http.GetFromJsonAsync<ApiResponse<PagedDocuments>>("/api/ingest/documents");
        return response?.Data?.Items;
    }

    public async Task<TenantSettingsDto?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("/api/admin/settings", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Settings API returned {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }

        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponse<TenantSettingsDto>>(cancellationToken);
        if (wrapper?.Data is null)
            throw new InvalidOperationException("Settings API returned an empty payload.");

        return wrapper.Data;
    }

    public async Task TriggerEvaluationAsync()
    {
        await _http.PostAsJsonAsync("/api/admin/evaluation/trigger", new { });
    }

    public async Task UploadDocumentAsync(Stream fileStream, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        content.Add(new StringContent("Contoso Policy Document"), "Title");
        content.Add(new StringContent("HR"), "Department");
        content.Add(new StringContent("policy"), "DocType");
        content.Add(new StringContent("Recursive"), "ChunkingStrategy");
        await _http.PostAsync("/api/ingest/documents", content);
    }
}

public record UsageMetrics(long TotalMessages, long TotalTokens, decimal EstimatedCostUsd, double AvgLatencyMs, double PositiveFeedbackRate);
public record PromptTemplate(Guid Id, string Name, string Category, int Version, bool IsActive);
public record DocumentItem(Guid Id, string FileName, string Status, string? Title, DateTimeOffset CreatedAt);
public record PagedDocuments(List<DocumentItem> Items, int TotalCount, int Page, int PageSize);
