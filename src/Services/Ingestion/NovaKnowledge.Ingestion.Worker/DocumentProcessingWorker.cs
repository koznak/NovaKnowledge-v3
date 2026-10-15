using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Ingestion;
using NovaKnowledge.Infrastructure.Configuration;
using NovaKnowledge.Infrastructure.Ingestion;

namespace NovaKnowledge.Ingestion.Worker;

public class DocumentProcessingWorker : BackgroundService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentProcessingWorker> _logger;

    public DocumentProcessingWorker(
        ServiceBusClient serviceBusClient,
        IOptions<ServiceBusOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentProcessingWorker> logger)
    {
        _serviceBusClient = serviceBusClient;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processor = _serviceBusClient.CreateProcessor(_options.DocumentUploadedQueue, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 2,
            AutoCompleteMessages = false
        });

        processor.ProcessMessageAsync += ProcessMessageAsync;
        processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Service Bus error: {ErrorSource}", args.ErrorSource);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);
        _logger.LogInformation("Document processing worker started on queue {Queue}", _options.DocumentUploadedQueue);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            await processor.StopProcessingAsync(CancellationToken.None);
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        DocumentUploadedEvent? message = null;
        try
        {
            message = JsonSerializer.Deserialize<DocumentUploadedEvent>(args.Message.Body.ToString());
            if (message == null) throw new InvalidOperationException("Invalid message payload");

            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<DocumentProcessor>();
            await processor.ProcessAsync(message, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process document {DocumentId}", message?.DocumentId);
            await args.DeadLetterMessageAsync(args.Message, "ProcessingFailed", ex.Message);
        }
    }
}
