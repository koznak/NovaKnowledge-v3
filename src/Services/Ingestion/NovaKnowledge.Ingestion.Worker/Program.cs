using NovaKnowledge.Ingestion.Worker;
using NovaKnowledge.Infrastructure;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Ingestion.Worker")
    .WriteTo.Console()
    .CreateLogger();

builder.Services.AddSerilog();
builder.Services.AddNovaKnowledgeInfrastructure(builder.Configuration, "Ingestion.Worker");
builder.Services.AddHostedService<DocumentProcessingWorker>();
builder.Services.AddScoped<DocumentProcessor>();

var host = builder.Build();
await host.Services.EnsureDatabaseAsync();
await host.RunAsync();
