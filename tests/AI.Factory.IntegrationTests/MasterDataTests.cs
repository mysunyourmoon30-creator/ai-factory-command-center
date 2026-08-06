using System.Net;
using System.Net.Http.Json;
using AI.Factory.Core.MasterData;
using AI.Factory.Infrastructure.MasterData;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

public sealed class MasterDataTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private readonly AiFactoryWebApplicationFactory _factory;
    public MasterDataTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Canonical_master_seed_is_idempotent_and_formulations_balance()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await CanonicalMasterDataSeeder.SeedAsync(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var canonicalCodes = Enumerable.Range(1, 10).Select(x => $"RM-{x:000}").ToArray();
        Assert.Equal(10, await db.RawMaterials.CountAsync(x => canonicalCodes.Contains(x.Code)));
        Assert.Equal(5, await db.Formulations.CountAsync());
        var formulations = await db.Formulations.Include(x => x.Materials).ToArrayAsync();
        Assert.All(formulations, x => Assert.Equal(x.BatchSize, x.Materials.Sum(m => m.WeightPerBatch)));
    }

    [Fact]
    public async Task Formulation_sum_must_equal_batch_size()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMasterDataService>();
        var raw = (await service.ListRawMaterialsAsync()).Take(2).ToArray();
        var command = new FormulationCommand("FM-BAD", "Invalid", 500, true,
            [new(raw[0].Id, 250), new(raw[1].Id, 200)]);
        var error = await Assert.ThrowsAsync<DomainValidationException>(() => service.CreateFormulationAsync(command));
        Assert.Contains("sum must equal", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stale_raw_material_row_version_returns_conflict_from_service()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMasterDataService>();
        var raw = (await service.ListRawMaterialsAsync()).First();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.UpdateRawMaterialAsync(raw.Id,
            new(raw.Code, raw.Name, raw.Unit, raw.CurrentStock, raw.ReservedStock, raw.LeadTimeDays, raw.IsActive, [1])));
    }

    [Fact]
    public async Task Viewer_can_read_master_data_but_cannot_create_it()
    {
        using var client = CreateClient();
        await LoginAsync(client, "viewer.demo");
        using var list = await client.GetAsync("/api/raw-materials");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var token = await GetTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/raw-materials")
        {
            Content = JsonContent.Create(new RawMaterialCommand("RM-X", "Blocked", "kg", 0, 0, 1, true))
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 but received {(int)response.StatusCode}: {body}");
    }

    /// <summary>Locked Test 3: viewer.demo calls Update Raw Material -> HTTP 403, no data change, Unauthorized audit.</summary>
    [Fact]
    public async Task Viewer_cannot_update_raw_material_and_no_data_changes()
    {
        using var client = CreateClient();
        await LoginAsync(client, "viewer.demo");
        var before = (await client.GetFromJsonAsync<RawMaterialDto[]>("/api/raw-materials"))!.First();

        var token = await GetTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/raw-materials/{before.Id}")
        {
            Content = JsonContent.Create(new RawMaterialCommand(before.Code, "Hacked By Viewer", before.Unit, before.CurrentStock + 999, before.ReservedStock, before.LeadTimeDays, before.IsActive, before.RowVersion))
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var after = (await client.GetFromJsonAsync<RawMaterialDto[]>("/api/raw-materials"))!.Single(x => x.Id == before.Id);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.CurrentStock, after.CurrentStock);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(x => x.Action == "Unauthorized Access" && x.Username == "viewer.demo"));
    }

    [Fact]
    public async Task Planner_can_create_raw_material_through_secured_api()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        var token = await GetTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/raw-materials")
        {
            Content = JsonContent.Create(new RawMaterialCommand("RM-TEST", "Test Material", "kg", 100, 10, 3, true))
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<RawMaterialDto>();
        Assert.NotNull(created);
        Assert.Equal(90, created.CurrentStock - created.ReservedStock);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
    private static async Task LoginAsync(HttpClient client, string username)
    {
        var token = await GetTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = username, ["Password"] = "Demo@12345", ["ReturnUrl"] = "/" }) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
    private static async Task<string> GetTokenAsync(HttpClient client) => (await client.GetFromJsonAsync<TokenResponse>("/api/auth/antiforgery"))!.Token;
    private sealed record TokenResponse(string Token);
}
