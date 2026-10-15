using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NovaKnowledge.Infrastructure.Configuration;

namespace NovaKnowledge.Infrastructure.Resilience;

/// <summary>
/// Readiness check that verifies the Azure AI Search endpoint is reachable and the index client
/// can be constructed. Tagged "ready" so it does not affect liveness probes.
/// </summary>
public sealed class AzureSearchHealthCheck(
    SearchIndexClient indexClient,
    IOptions<AzureSearchOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await indexClient.GetIndexAsync(options.Value.IndexName, cancellationToken);
            return HealthCheckResult.Healthy("Azure AI Search reachable.");
        }
        catch (global::Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Index not created yet is not a readiness failure of the dependency itself.
            return HealthCheckResult.Degraded("Azure AI Search reachable but index is not created.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Azure AI Search unreachable.", ex);
        }
    }
}
