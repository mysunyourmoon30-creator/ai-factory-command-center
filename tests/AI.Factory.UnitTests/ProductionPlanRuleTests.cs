using AI.Factory.Core.Production;

namespace AI.Factory.UnitTests;

public sealed class ProductionPlanRuleTests
{
    [Theory]
    [InlineData(10_000, 500, 20)]
    [InlineData(1_001, 500, 3)]
    [InlineData(800, 400, 2)]
    public void Required_batch_always_rounds_up(decimal quantity, decimal batchSize, int expected) =>
        Assert.Equal(expected, ProductionPlanRules.CalculateRequiredBatch(quantity, batchSize));

    [Fact]
    public void Required_material_is_batch_count_times_recipe_weight() =>
        Assert.Equal(5_000, ProductionPlanRules.CalculateRequiredMaterial(20, 250));
}
