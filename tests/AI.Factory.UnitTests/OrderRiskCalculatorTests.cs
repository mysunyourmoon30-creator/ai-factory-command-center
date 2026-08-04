using AI.Factory.Core.Domain;
using AI.Factory.Core.Orders;

namespace AI.Factory.UnitTests;

public sealed class OrderRiskCalculatorTests
{
    private readonly OrderRiskCalculator _calculator = new();
    private static readonly DateTime Delivery = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-2, RiskStatus.Critical)]
    [InlineData(0, RiskStatus.Warning)]
    [InlineData(1, RiskStatus.Warning)]
    [InlineData(2, RiskStatus.Normal)]
    public void Timing_risk_uses_locked_buffer_boundaries(int bufferDays, RiskStatus expected)
    {
        var completion = Delivery.AddDays(-bufferDays);
        Assert.Equal(expected, _calculator.Calculate(Delivery, completion));
    }

    [Fact]
    public void Order_without_plan_has_no_timing_risk() =>
        Assert.Equal(RiskStatus.Normal, _calculator.Calculate(Delivery, null));
}
