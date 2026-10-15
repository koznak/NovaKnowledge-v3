using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using NovaKnowledge.Application.Chunking;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Infrastructure.Agents;
using NovaKnowledge.Infrastructure.Azure;
using NovaKnowledge.Infrastructure.Configuration;
using NovaKnowledge.Infrastructure.Extraction;
using NovaKnowledge.Infrastructure.Ingestion;
using NovaKnowledge.Infrastructure.Persistence;
using NovaKnowledge.Infrastructure.Repositories;
using NovaKnowledge.Infrastructure.Resilience;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace NovaKnowledge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNovaKnowledgeInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        services.Configure<AzureOpenAIOptions>(configuration.GetSection(AzureOpenAIOptions.SectionName));
        services.Configure<AzureSearchOptions>(configuration.GetSection(AzureSearchOptions.SectionName));
        services.Configure<AzureStorageOptions>(configuration.GetSection(AzureStorageOptions.SectionName));
        services.Configure<ServiceBusOptions>(configuration.GetSection(ServiceBusOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<ServiceUrlsOptions>(configuration.GetSection(ServiceUrlsOptions.SectionName));
        services.Configure<NovaKnowledgeOptions>(configuration.GetSection(NovaKnowledgeOptions.SectionName));
        services.Configure<PricingOptions>(configuration.GetSection(PricingOptions.SectionName));

        var connectionString = configuration.GetConnectionString("NovaKnowledgeDb")
            ?? "Host=localhost;Port=5432;Database=novaknowledge;Username=postgres;Password=postgres";

        services.AddDbContext<NovaKnowledgeDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddKeyedSingleton<global::Azure.AI.OpenAI.AzureOpenAIClient>("chat", (sp, _) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureOpenAIOptions>>().Value;
            return AzureClientFactory.CreateOpenAIClient(opts);
        });

        services.AddKeyedSingleton<global::Azure.AI.OpenAI.AzureOpenAIClient>("embedding", (sp, _) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureOpenAIOptions>>().Value;
            return AzureClientFactory.CreateEmbeddingClient(opts);
        });

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureSearchOptions>>().Value;
            return new SearchIndexClient(new Uri(opts.Endpoint), new AzureKeyCredential(opts.ApiKey));
        });

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureSearchOptions>>().Value;
            return new SearchClient(new Uri(opts.Endpoint), opts.IndexName, new AzureKeyCredential(opts.ApiKey));
        });

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureStorageOptions>>().Value;
            return new BlobServiceClient(opts.ConnectionString);
        });

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceBusOptions>>().Value;
            return new global::Azure.Messaging.ServiceBus.ServiceBusClient(opts.ConnectionString);
        });

        var redisConnection = configuration.GetSection(RedisOptions.SectionName)["ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
                StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnection));
            services.AddSingleton<ICacheService, RedisCacheService>();
        }
        else
        {
            services.AddSingleton<ICacheService, NullCacheService>();
        }

        services.AddScoped<IEmbeddingService, AzureOpenAIEmbeddingService>();
        services.AddScoped<IChatCompletionService, AzureOpenAIChatService>();
        services.AddScoped<ISearchIndexService, AzureSearchIndexService>();
        services.AddScoped<ISearchQueryService, AzureSearchQueryService>();
        services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
        services.AddScoped<IMessagePublisher, ServiceBusMessagePublisher>();
        services.AddScoped<ITextExtractionService, TextExtractionService>();
        services.AddScoped<IChunkingService, RecursiveChunkingService>();
        services.AddScoped<DocumentProcessor>();
        services.AddScoped<IPromptTemplateService, PromptTemplateService>();

        // Semantic Kernel: a single shared Kernel wired to the configured chat deployment.
        services.AddScoped(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureOpenAIOptions>>().Value;
            var builder = Kernel.CreateBuilder();
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: opts.ChatDeployment,
                azureOpenAIClient: AzureClientFactory.CreateOpenAIClient(opts));
            return builder.Build();
        });

        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();

        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IFeedbackRepository, FeedbackRepository>();
        services.AddScoped<IPromptTemplateRepository, PromptTemplateRepository>();
        services.AddScoped<IEvaluationRunRepository, EvaluationRunRepository>();
        services.AddScoped<ITenantSettingsRepository, TenantSettingsRepository>();
        services.AddScoped<IUsageMetricsRepository, UsageMetricsRepository>();

        services.AddNovaKnowledgeResilience();

        services.AddHttpClient("orchestration")
            .AddPolicyHandler(PollyPolicies.GetRetryPolicy())
            .AddPolicyHandler(PollyPolicies.GetCircuitBreakerPolicy());

        services.AddHttpClient("search")
            .AddPolicyHandler(PollyPolicies.GetRetryPolicy())
            .AddPolicyHandler(PollyPolicies.GetCircuitBreakerPolicy());

        services.AddHttpClient<IEvaluationClient, HttpEvaluationClient>((sp, client) =>
        {
            var urls = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceUrlsOptions>>().Value;
            client.BaseAddress = new Uri(urls.EvaluationService);
        });

        services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "postgresql", tags: ["ready"])
            .AddCheck<AzureSearchHealthCheck>("azure-search", tags: ["ready"])
            .AddCheck("self", () => HealthCheckResult.Healthy(serviceName));

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("NovaKnowledge")
                .AddOtlpExporter());

        return services;
    }

    public static WebApplicationBuilder AddNovaKnowledgeSerilog(this WebApplicationBuilder builder, string serviceName)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .WriteTo.Console()
            .CreateLogger();

        builder.Host.UseSerilog();
        return builder;
    }

    public static WebApplication MapNovaKnowledgeHealthChecks(this WebApplication app)
    {
        // Liveness: process is up. Does not depend on external services.
        app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Name == "self"
        });
        // Readiness: external dependencies (database, Azure AI Search) are reachable.
        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });
        app.MapGet("/ready", () => Results.Ok(new { status = "ready", timestamp = DateTimeOffset.UtcNow }));
        return app;
    }

    /// <summary>Registers the RFC 7807 exception-handling middleware. Call early in the pipeline.</summary>
    public static WebApplication UseNovaKnowledgeExceptionHandling(this WebApplication app)
    {
        app.UseExceptionHandler();
        return app;
    }

    public static async Task EnsureDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovaKnowledgeDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NovaKnowledgeDbContext>>();
        var connectionString = configuration.GetConnectionString("NovaKnowledgeDb")
            ?? "Host=localhost;Port=5432;Database=novaknowledge;Username=postgres;Password=postgres";

        await DatabaseBootstrap.EnsureDatabaseAsync(db, connectionString, logger);
    }
}

public class NullCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
