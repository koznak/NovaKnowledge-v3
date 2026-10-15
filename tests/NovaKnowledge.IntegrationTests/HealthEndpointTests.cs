using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace NovaKnowledge.IntegrationTests;

/// <summary>
/// Boots the real Search.Api composition root via <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// The database bootstrap and Redis/Azure clients are stubbed so the test only exercises the
/// HTTP pipeline (routing, middleware, health endpoints), not live dependencies.
/// </summary>
public class SearchApiFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Replace the cache with the no-op implementation so no Redis is required.
            services.RemoveAll<NovaKnowledge.Application.Interfaces.ICacheService>();
            services.AddSingleton<NovaKnowledge.Application.Interfaces.ICacheService, NovaKnowledge.Infrastructure.NullCacheService>();
        });
        return base.CreateHost(builder);
    }
}

public class HealthEndpointTests : IClassFixture<SearchApiFactory>
{
    private readonly SearchApiFactory _factory;

    public HealthEndpointTests(SearchApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_endpoint_returns_ok_with_status_payload()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_route_returns_404_not_500()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
