using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace NovaKnowledge.Infrastructure.Resilience;

public static class NovaKnowledgeMetrics
{
    public static readonly Meter Meter = new("NovaKnowledge", "1.0.0");
    public static readonly Counter<long> TokensUsed = Meter.CreateCounter<long>("tokens_used");
    public static readonly Counter<long> ChatRequests = Meter.CreateCounter<long>("chat_requests");
    public static readonly Histogram<double> RetrievalLatency = Meter.CreateHistogram<double>("retrieval_latency_ms");
    public static readonly Histogram<double> LlmLatency = Meter.CreateHistogram<double>("llm_latency_ms");
    public static readonly Counter<double> EstimatedCost = Meter.CreateCounter<double>("estimated_cost_usd");
}

public static class PollyPolicies
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

    public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
}

public static class ResilienceExtensions
{
    public static IServiceCollection AddNovaKnowledgeResilience(this IServiceCollection services)
    {
        services.AddSingleton(NovaKnowledgeMetrics.Meter);
        return services;
    }
}
