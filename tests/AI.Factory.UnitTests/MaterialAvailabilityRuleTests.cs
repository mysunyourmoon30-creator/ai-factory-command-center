using AI.Factory.Core.Domain;
using AI.Factory.Core.Production;

namespace AI.Factory.UnitTests;

public sealed class MaterialAvailabilityRuleTests
{
    private static readonly DateTime Today = new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ProductionPlanStatus.Planned, true)]
    [InlineData(ProductionPlanStatus.InProduction, true)]
    [InlineData(ProductionPlanStatus.Completed, false)]
    public void Only_planned_and_in_production_count_as_active_demand(ProductionPlanStatus status, bool expected) =>
        Assert.Equal(expected, MaterialAvailabilityRules.IsActiveDemand(status));

    [Fact]
    public void On_hand_available_keeps_a_negative_result()
    {
        var onHandAvailable = MaterialAvailabilityRules.CalculateOnHandAvailable(400, 450);

        Assert.Equal(-50, onHandAvailable);
        Assert.Equal(-50, MaterialAvailabilityRules.CalculateAvailableByDate(onHandAvailable, 0));
    }

    [Fact]
    public void Cumulative_required_sums_every_demand_up_to_the_date()
    {
        MaterialDemand[] demands = [new(Today.AddDays(1), 80), new(Today.AddDays(3), 70)];

        Assert.Equal(0, MaterialAvailabilityRules.CalculateCumulativeRequired(demands, Today));
        Assert.Equal(80, MaterialAvailabilityRules.CalculateCumulativeRequired(demands, Today.AddDays(1)));
        Assert.Equal(80, MaterialAvailabilityRules.CalculateCumulativeRequired(demands, Today.AddDays(2)));
        Assert.Equal(150, MaterialAvailabilityRules.CalculateCumulativeRequired(demands, Today.AddDays(3)));
    }

    [Fact]
    public void Incoming_supply_is_eligible_only_between_today_and_the_evaluated_date()
    {
        IncomingSupply[] supplies = [new(Today.AddDays(-4), 1_000), new(Today.AddDays(3), 50), new(Today.AddDays(9), 400)];

        Assert.Equal(0, MaterialAvailabilityRules.CalculateCumulativeIncoming(supplies, Today.AddDays(1), Today));
        Assert.Equal(50, MaterialAvailabilityRules.CalculateCumulativeIncoming(supplies, Today.AddDays(3), Today));
        Assert.Equal(450, MaterialAvailabilityRules.CalculateCumulativeIncoming(supplies, Today.AddDays(9), Today));
    }

    [Fact]
    public void Fully_received_supply_is_not_counted()
    {
        var outstanding = MaterialAvailabilityRules.CalculateOutstandingQuantity(600, 600);
        IncomingSupply[] supplies = [new(Today.AddDays(2), outstanding)];

        Assert.Equal(0, outstanding);
        Assert.Equal(0, MaterialAvailabilityRules.CalculateCumulativeIncoming(supplies, Today.AddDays(2), Today));
    }

    [Fact]
    public void Available_exactly_meeting_cumulative_demand_is_not_a_deficit()
    {
        MaterialDemand[] demands = [new(Today.AddDays(1), 80), new(Today.AddDays(3), 70)];
        IncomingSupply[] supplies = [new(Today.AddDays(3), 50)];

        var timeline = MaterialAvailabilityRules.BuildTimeline(100, demands, supplies, Today);

        Assert.Equal(150, timeline[^1].CumulativeRequired);
        Assert.Equal(150, timeline[^1].AvailableByDate);
    }

    [Fact]
    public void Timeline_evaluates_each_required_date_without_collapsing_to_the_earliest()
    {
        MaterialDemand[] demands = [new(Today.AddDays(3), 70), new(Today.AddDays(1), 80)];
        IncomingSupply[] supplies = [new(Today.AddDays(3), 50)];

        var timeline = MaterialAvailabilityRules.BuildTimeline(100, demands, supplies, Today);

        Assert.Equal(2, timeline.Count);
        Assert.Equal(Today.AddDays(1), timeline[0].RequiredDate);
        Assert.Equal(80, timeline[0].CumulativeRequired);
        Assert.Equal(0, timeline[0].CumulativeIncoming);
        Assert.Equal(100, timeline[0].AvailableByDate);
        Assert.Equal(Today.AddDays(3), timeline[1].RequiredDate);
        Assert.Equal(50, timeline[1].CumulativeIncoming);
        Assert.Equal(150, timeline[1].AvailableByDate);
    }

    [Fact]
    public void Repeated_required_dates_collapse_into_one_evaluation_point()
    {
        MaterialDemand[] demands = [new(Today.AddDays(2), 30), new(Today.AddDays(2), 45)];

        var timeline = MaterialAvailabilityRules.BuildTimeline(100, demands, [], Today);

        Assert.Equal(75, Assert.Single(timeline).CumulativeRequired);
    }

    [Fact]
    public void No_active_demand_produces_an_empty_timeline()
    {
        Assert.Empty(MaterialAvailabilityRules.BuildTimeline(100, [], [new(Today.AddDays(2), 500)], Today));
    }
}
