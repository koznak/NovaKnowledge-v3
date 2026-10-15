using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NovaKnowledge.Contracts.Chat;
using NovaKnowledge.Infrastructure.Resilience;

namespace NovaKnowledge.Infrastructure.Chat;

/// <summary>
/// Writes Server-Sent Events for the chat stream and records chat metrics.
/// Centralises the SSE framing so the controller stays focused on orchestration.
/// </summary>
public static class ChatStreamWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(HttpResponse response, ChatStreamChunk chunk, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(chunk, JsonOptions);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);

        if (chunk.Type == "done")
            NovaKnowledgeMetrics.ChatRequests.Add(1);
    }

    /// <summary>Prepares the response for an SSE stream.</summary>
    public static void Prepare(HttpResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
    }

    /// <summary>Records retrieval and LLM latency/cost metrics for a completed chat turn.</summary>
    public static void RecordTurnMetrics(int retrievalLatencyMs, int llmLatencyMs, int promptTokens, int completionTokens, decimal estimatedCostUsd)
    {
        NovaKnowledgeMetrics.RetrievalLatency.Record(retrievalLatencyMs);
        NovaKnowledgeMetrics.LlmLatency.Record(llmLatencyMs);
        NovaKnowledgeMetrics.TokensUsed.Add(promptTokens + completionTokens);
        NovaKnowledgeMetrics.EstimatedCost.Add((double)estimatedCostUsd);
    }
}
