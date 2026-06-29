using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Ingestion;
using NovaKnowledge.Infrastructure.Configuration;

namespace NovaKnowledge.Infrastructure.Azure;

public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _client;
    private readonly AzureStorageOptions _options;

    public AzureBlobStorageService(BlobServiceClient client, IOptions<AzureStorageOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<string> UploadAsync(string container, string blobName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var containerClient = _client.GetBlobContainerClient(container);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: cancellationToken);
        return blobClient.Uri.ToString();
    }

    public async Task<Stream> DownloadAsync(string container, string blobPath, CancellationToken cancellationToken = default)
    {
        var containerClient = _client.GetBlobContainerClient(container);
        var blobClient = containerClient.GetBlobClient(blobPath);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    public async Task<string> UploadJsonlAsync(string container, string blobName, IEnumerable<string> lines, CancellationToken cancellationToken = default)
    {
        var content = string.Join('\n', lines);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await UploadAsync(container, blobName, stream, "application/x-ndjson", cancellationToken);
    }

    public static string ComputeContentHash(Stream stream)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public class ServiceBusMessagePublisher : IMessagePublisher
{
    private readonly global::Azure.Messaging.ServiceBus.ServiceBusClient _client;
    private readonly ServiceBusOptions _options;

    public ServiceBusMessagePublisher(global::Azure.Messaging.ServiceBus.ServiceBusClient client, IOptions<ServiceBusOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task PublishDocumentUploadedAsync(DocumentUploadedEvent message, CancellationToken cancellationToken = default)
    {
        var sender = _client.CreateSender(_options.DocumentUploadedQueue);
        var sbMessage = new global::Azure.Messaging.ServiceBus.ServiceBusMessage(System.Text.Json.JsonSerializer.Serialize(message))
        {
            MessageId = message.DocumentId.ToString(),
            Subject = "DocumentUploaded"
        };
        await sender.SendMessageAsync(sbMessage, cancellationToken);
    }
}

public class RedisCacheService : ICacheService
{
    private readonly StackExchange.Redis.IDatabase _db;

    public RedisCacheService(StackExchange.Redis.IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var value = await _db.StringGetAsync(key);
        if (value.IsNullOrEmpty) return default;
        return System.Text.Json.JsonSerializer.Deserialize<T>(value.ToString());
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        await _db.StringSetAsync(key, json, expiry);
    }
}
