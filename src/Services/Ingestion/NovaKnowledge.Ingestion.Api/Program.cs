var builder = WebApplication.CreateBuilder(args);
builder.AddNovaKnowledgeSerilog("Ingestion.Api");
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() { Title = "NovaKnowledge Ingestion API", Version = "v1" }));
builder.Services.AddNovaKnowledgeInfrastructure(builder.Configuration, "Ingestion.Api");

var app = builder.Build();
await app.Services.EnsureDatabaseAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.MapControllers();
app.MapNovaKnowledgeHealthChecks();
app.Run();
