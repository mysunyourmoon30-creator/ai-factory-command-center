using System.Net;
using System.Net.Http.Json;
using System.Text;
using AI.Factory.Core.Domain;
using AI.Factory.Core.Production;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

public sealed class ReportTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private static readonly DateTime TestTime = new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Utf8Bom = Encoding.UTF8.GetPreamble();
    private readonly AiFactoryWebApplicationFactory _factory;
    public ReportTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Json_report_endpoints_reuse_the_same_data_as_their_feature_screens()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");

        var risk = await client.GetFromJsonAsync<ProductionPlanDto[]>("/api/reports/production-risk");
        var shortage = await client.GetFromJsonAsync<MaterialShortageDto[]>("/api/reports/material-shortage");
        var purchaseOrders = await client.GetFromJsonAsync<IncomingPurchaseOrderDto[]>("/api/reports/purchase-order-status");

        // Other tests in this class add fixture rows against the same shared host, so assert
        // presence of the canonical seed rather than an exact count that depends on test order.
        Assert.Contains(risk!, x => x.PlanNumber == "PP-DEMO-001");
        Assert.Contains(shortage!, x => x.RawMaterialCode == "RM-001");
        Assert.NotNull(purchaseOrders);
    }

    /// <summary>
    /// vw_ProductionRiskReport is a real SQL Server VIEW, created via raw migration SQL. The EF
    /// Core InMemory provider used by this test host has no SQL engine to execute it, so a
    /// keyless ToView entity always returns zero rows here regardless of seed data — this is a
    /// structural InMemory limitation, not an app bug (the same category as the Serializable
    /// and RowVersion-regeneration gaps already documented for Days 7-8). This test can only
    /// verify the endpoint wiring (status, content type, header row); row content and the
    /// injection defenses for this view are verified live against LocalDB.
    /// </summary>
    [Fact]
    public async Task Production_risk_csv_endpoint_returns_a_well_formed_header()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        using var response = await client.GetAsync("/api/reports/production-risk/export.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType!.ToString());
        var csv = await ReadCsvTextAsync(response);
        Assert.StartsWith("Plan Number,Order Number,Formulation,Machine,Required Batch,Planned Completion,Lifecycle Status,Risk Status\r\n", csv);
    }

    [Fact]
    public async Task Material_shortage_csv_neutralizes_a_formula_and_a_comma()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RawMaterials.Add(new RawMaterial { Code = "RM-INJECT-A", Name = "=1+1", Unit = "kg", CurrentStock = 10, ReservedStock = 0, LeadTimeDays = 1, CreatedAt = TestTime, UpdatedAt = TestTime });
            db.RawMaterials.Add(new RawMaterial { Code = "RM-INJECT-B", Name = "Acme, Inc", Unit = "kg", CurrentStock = 10, ReservedStock = 0, LeadTimeDays = 1, CreatedAt = TestTime, UpdatedAt = TestTime });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        using var response = await client.GetAsync("/api/reports/material-shortage/export.csv");
        var csv = await ReadCsvTextAsync(response);

        Assert.Contains("'=1+1", csv);
        Assert.Contains("\"RM-INJECT-B - Acme, Inc\"", csv);
    }

    /// <summary>
    /// vw_PurchaseOrderStatusReport has the same InMemory limitation as the production-risk view
    /// (see the comment on Production_risk_csv_endpoint_returns_a_well_formed_header) — row
    /// content and injection defenses for this view are verified live against LocalDB.
    /// </summary>
    [Fact]
    public async Task Purchase_order_status_csv_endpoint_returns_a_well_formed_header()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        using var response = await client.GetAsync("/api/reports/purchase-order-status/export.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType!.ToString());
        var csv = await ReadCsvTextAsync(response);
        Assert.StartsWith("Purchase Order,Request,Expected Date,Received Date,Status,Ordered Quantity,Received Quantity,Late,Delay Days\r\n", csv);
    }

    [Theory]
    [InlineData("admin.demo")]
    [InlineData("manager.demo")]
    [InlineData("planner.demo")]
    [InlineData("viewer.demo")]
    public async Task Every_role_can_export_reports(string username)
    {
        using var client = CreateClient();
        await LoginAsync(client, username);

        using var response = await client.GetAsync("/api/reports/material-shortage/export.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_export_is_rejected()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/reports/material-shortage/export.csv");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static void AssertUtf8Bom(byte[] bytes) => Assert.Equal(Utf8Bom, bytes[..Utf8Bom.Length]);

    private static async Task<string> ReadCsvTextAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        AssertUtf8Bom(bytes);
        return Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });

    private static async Task LoginAsync(HttpClient client, string username)
    {
        var token = (await client.GetFromJsonAsync<TokenResponse>("/api/auth/antiforgery"))!.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = username, ["Password"] = "Demo@12345", ["ReturnUrl"] = "/" })
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private sealed record TokenResponse(string Token);
}
