using AI.Factory.Core.Time;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.UnitTests;

public sealed class FoundationContractTests
{
    [Fact]
    public void Model_contains_exactly_fourteen_application_tables()
    {
        using var context = CreateContext();
        var applicationTables = context.Model.GetEntityTypes()
            .Select(x => x.GetTableName())
            .Where(x => x is not null && !x.StartsWith("AspNet", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(14, applicationTables.Length);
    }

    [Fact]
    public void Purchase_request_source_plan_has_no_single_column_unique_index()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Core.Domain.PurchaseRequest));
        Assert.NotNull(entity);

        var forbiddenIndex = entity.GetIndexes().SingleOrDefault(index =>
            index.IsUnique &&
            index.Properties.Count == 1 &&
            index.Properties[0].Name == "SourceProductionPlanId");

        Assert.Null(forbiddenIndex);
    }

    [Fact]
    public void Fixed_time_provider_returns_canonical_time()
    {
        var canonicalTime = new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);
        var provider = new FixedTimeProvider(canonicalTime);

        Assert.Equal(canonicalTime, provider.GetUtcNow());
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=AI_Factory_ModelOnly;Trusted_Connection=True")
            .Options;

        return new AppDbContext(options);
    }
}
