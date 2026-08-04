using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AI.Factory.IntegrationTests;

public sealed class HostSmokeTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private readonly AiFactoryWebApplicationFactory _factory;

    public HostSmokeTests(AiFactoryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Live_health_endpoint_returns_only_healthy_status()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"Healthy\"}", body);
    }
}
