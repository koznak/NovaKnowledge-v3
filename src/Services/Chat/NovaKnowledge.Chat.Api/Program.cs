var builder = WebApplication.CreateBuilder(args);
builder.AddNovaKnowledgeSerilog("Chat.Api");
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() { Title = "NovaKnowledge Chat API", Version = "v1" }));
builder.Services.AddNovaKnowledgeInfrastructure(builder.Configuration, "Chat.Api");

var app = builder.Build();
await app.Services.EnsureDatabaseAsync();

app.UseNovaKnowledgeExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.MapControllers();
app.MapNovaKnowledgeHealthChecks();
app.Run();
