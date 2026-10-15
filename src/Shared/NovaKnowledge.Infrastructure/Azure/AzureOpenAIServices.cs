using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Infrastructure.Configuration;
using OpenAI.Embeddings;

namespace NovaKnowledge.Infrastructure.Azure;

public class AzureOpenAIEmbeddingService : IEmbeddingService
{
    private readonly AzureOpenAIClient _client;
    private readonly AzureOpenAIOptions _options;
    private readonly ILogger<AzureOpenAIEmbeddingService> _logger;

    public AzureOpenAIEmbeddingService(
        [FromKeyedServices("embedding")] AzureOpenAIClient client,
        IOptions<AzureOpenAIOptions> options,
        ILogger<AzureOpenAIEmbeddingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var results = await GenerateEmbeddingsAsync([text], cancellationToken);
        return results[0];
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0) return [];

        var embeddingClient = _client.GetEmbeddingClient(_options.EmbeddingDeployment);
        var results = new List<float[]>();

        const int batchSize = 16;
        for (var i = 0; i < texts.Count; i += batchSize)
        {
            var batch = texts.Skip(i).Take(batchSize).ToList();
            var response = await embeddingClient.GenerateEmbeddingsAsync(batch, cancellationToken: cancellationToken);
            foreach (var embedding in response.Value)
            {
                results.Add(embedding.ToFloats().ToArray());
            }
        }

        _logger.LogDebug("Generated {Count} embeddings", results.Count);
        return results;
    }
}

public class AzureOpenAIChatService : IChatCompletionService
{
    private readonly AzureOpenAIClient _client;
    private readonly AzureOpenAIOptions _options;
    private readonly ILogger<AzureOpenAIChatService> _logger;

    public AzureOpenAIChatService(
        [FromKeyedServices("chat")] AzureOpenAIClient client,
        IOptions<AzureOpenAIOptions> options,
        ILogger<AzureOpenAIChatService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(string Content, int PromptTokens, int CompletionTokens)> CompleteAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var chatClient = _client.GetChatClient(_options.ChatDeployment);
        var messages = new List<OpenAI.Chat.ChatMessage>
        {
            new OpenAI.Chat.SystemChatMessage(systemPrompt),
            new OpenAI.Chat.UserChatMessage(userPrompt)
        };

        try
        {
            var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            var content = response.Value.Content[0].Text ?? string.Empty;
            var usage = response.Value.Usage;
            return (content, usage?.InputTokenCount ?? 0, usage?.OutputTokenCount ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI chat completion failed for deployment {Deployment}", _options.ChatDeployment);
            throw;
        }
    }

    public async IAsyncEnumerable<ChatStreamUpdate> StreamAsync(
        string systemPrompt, string userPrompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatClient = _client.GetChatClient(_options.ChatDeployment);
        var messages = new List<OpenAI.Chat.ChatMessage>
        {
            new OpenAI.Chat.SystemChatMessage(systemPrompt),
            new OpenAI.Chat.UserChatMessage(userPrompt)
        };

        var options = new OpenAI.Chat.ChatCompletionOptions { EndUserId = null };

        await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, options, cancellationToken))
        {
            var delta = update.ContentUpdate is { Count: > 0 }
                ? string.Concat(update.ContentUpdate.Select(p => p.Text))
                : null;

            var usage = update.Usage;
            if (usage is not null)
            {
                yield return new ChatStreamUpdate(null, usage.InputTokenCount, usage.OutputTokenCount, IsFinal: true);
                continue;
            }

            if (!string.IsNullOrEmpty(delta))
                yield return new ChatStreamUpdate(delta, null, null, IsFinal: false);
        }
    }
}

public static class AzureClientFactory
{
    public static AzureOpenAIClient CreateOpenAIClient(AzureOpenAIOptions options) =>
        CreateOpenAIClient(options.Endpoint, options.ApiKey);

    public static AzureOpenAIClient CreateEmbeddingClient(AzureOpenAIOptions options) =>
        CreateOpenAIClient(options.ResolveEmbeddingEndpoint(), options.ResolveEmbeddingApiKey());

    public static AzureOpenAIClient CreateOpenAIClient(string endpoint, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        }

        return new AzureOpenAIClient(new Uri(endpoint), new global::Azure.Identity.DefaultAzureCredential());
    }
}
