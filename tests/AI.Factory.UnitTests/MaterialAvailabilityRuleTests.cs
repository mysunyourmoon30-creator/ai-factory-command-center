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

    [Fact]
    public void Demo_material_is_short_one_thousand_two_hundred_fifty_at_the_required_date()
    {
        var requiredDate = Today.AddDays(5);
        MaterialDemand[] demands = [new(requiredDate, 5_000)];
        IncomingSupply[] supplies = [new(Today.AddDays(-4), 1_000)];

        var timeline = MaterialAvailabilityRules.BuildTimeline(3_750, demands, supplies, Today);
        var evaluation = MaterialAvailabilityRules.Evaluate(3_750, timeline);

        Assert.Equal(1_250, evaluation.ShortageQuantity);
        Assert.Equal(requiredDate, evaluation.MaterialRequiredDate);
        Assert.Equal(requiredDate, evaluation.EvaluationDate);
        Assert.Equal(5_000, evaluation.CumulativeRequired);
        Assert.Equal(0, evaluation.CumulativeIncoming);
        Assert.Equal(3_750, evaluation.AvailableByDate);
    }

    [Fact]
    public void Several_plans_covered_by_later_supply_raise_no_shortage()
    {
        MaterialDemand[] demands = [new(Today.AddDays(1), 80), new(Today.AddDays(3), 70)];
        IncomingSupply[] supplies = [new(Today.AddDays(3), 50)];

        var timeline = MaterialAvailabilityRules.BuildTimeline(100, demands, supplies, Today);
        var evaluation = MaterialAvailabilityRules.Evaluate(100, timeline);

        Assert.Equal(0, evaluation.ShortageQuantity);
        Assert.Null(evaluation.MaterialRequiredDate);
        Assert.Equal(Today.AddDays(3), evaluation.EvaluationDate);
        Assert.Equal(150, evaluation.CumulativeRequired);
        Assert.Equal(150, evaluation.AvailableByDate);
    }

    [Fact]
    public void Evaluation_date_is_the_first_date_reaching_the_largest_deficit()
    {
        MaterialDemand[] demands = [new(Today.AddDays(1), 150), new(Today.AddDays(4), 0)];

        var timeline = MaterialAvailabilityRules.BuildTimeline(100, demands, [], Today);
        var evaluation = MaterialAvailabilityRules.Evaluate(100, timeline);

        Assert.Equal(50, evaluation.ShortageQuantity);
        Assert.Equal(Today.AddDays(1), evaluation.MaterialRequiredDate);
        Assert.Equal(Today.AddDays(1), evaluation.EvaluationDate);
    }

    [Fact]
    public void Shortage_grows_to_the_worst_date_not_the_first_one()
    {
        MaterialDemand[] demands = [new(Today.AddDays(1), 120), new(Today.AddDays(4), 200)];

        var evaluation = MaterialAvailabilityRules.Evaluate(100, MaterialAvailabilityRules.BuildTimeline(100, demands, [], Today));

        Assert.Equal(220, evaluation.ShortageQuantity);
        Assert.Equal(Today.AddDays(1), evaluation.MaterialRequiredDate);
        Assert.Equal(Today.AddDays(4), evaluation.EvaluationDate);
    }

    [Fact]
    public void No_active_demand_reports_the_unchanged_on_hand_position()
    {
        var evaluation = MaterialAvailabilityRules.Evaluate(-50, []);

        Assert.Equal(0, evaluation.ShortageQuantity);
        Assert.Null(evaluation.EvaluationDate);
        Assert.Null(evaluation.MaterialRequiredDate);
        Assert.Equal(-50, evaluation.AvailableByDate);
    }

    [Theory]
    [InlineData(PurchaseRequestStatus.Draft, true)]
    [InlineData(PurchaseRequestStatus.PendingApproval, true)]
    [InlineData(PurchaseRequestStatus.Approved, false)]
    [InlineData(PurchaseRequestStatus.Rejected, false)]
    public void Only_draft_and_pending_requests_block_a_new_one(PurchaseRequestStatus status, bool expected) =>
        Assert.Equal(expected, PurchaseRequestRules.IsActive(status));

    [Fact]
    public void Purchase_order_is_late_once_its_expected_date_passes_with_quantity_outstanding()
    {
        Assert.True(PurchaseRequestRules.IsLate(IncomingPurchaseOrderStatus.Open, Today.AddDays(-4), Today, 1_000));
        Assert.Equal(4, PurchaseRequestRules.CalculateDelayDays(Today.AddDays(-4), Today));
        Assert.False(PurchaseRequestRules.IsLate(IncomingPurchaseOrderStatus.Open, Today.AddDays(2), Today, 1_000));
        Assert.False(PurchaseRequestRules.IsLate(IncomingPurchaseOrderStatus.Received, Today.AddDays(-4), Today, 0));
    }
}
