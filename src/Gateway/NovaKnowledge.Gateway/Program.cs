using System.Threading.RateLimiting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Gateway")
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var rateLimitPermits = builder.Configuration.GetValue("Gateway:RateLimitPermitsPerMinute", 100);
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddRateLimiter(options =>
    {
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitPermits,
                    Window = TimeSpan.FromMinutes(1)
                }));
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    });
}

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "NovaKnowledge Gateway", Version = "v1" });
    c.AddServer(new Microsoft.OpenApi.Models.OpenApiServer { Url = "/" });
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? Guid.NewGuid().ToString();
    context.Response.Headers["X-Correlation-Id"] = correlationId;
    context.Items["CorrelationId"] = correlationId;
    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});

if (!app.Environment.IsDevelopment())
{
    app.UseRateLimiter();
}

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "NovaKnowledge Gateway");
        c.RoutePrefix = "swagger";
    });
}

app.MapGet("/", () => Results.Ok(new
{
    service = "NovaKnowledge Gateway",
    version = "1.0.0",
    endpoints = new[] { "/api/chat", "/api/ingest", "/api/search", "/api/feedback", "/api/admin", "/api/orchestration" }
}));

app.MapHealthChecks("/health");
app.MapGet("/ready", () => Results.Ok(new { status = "ready" }));

app.MapReverseProxy();
app.Run();
