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

    /// <summary>
    /// The example-based tests above each pin one scenario. This pins the *properties* those examples
    /// are instances of, across 500 randomly generated demand/supply sets - including the awkward
    /// shapes nobody writes by hand: no demand at all, zero-quantity demand, supply and demand dated
    /// in the past, a negative on-hand position, and repeated dates.
    ///
    /// The seed is fixed, so a failure here is reproducible rather than a heisenbug, and the failure
    /// message carries the iteration that broke.
    ///
    /// Added during the bug audit of the shortage engine. It found nothing - which is the useful
    /// result to record for the calculation the whole system is built around.
    /// </summary>
    [Fact]
    public void Timeline_and_evaluation_hold_their_invariants_for_arbitrary_demand_and_supply()
    {
        var random = new Random(20260831);

        for (var iteration = 0; iteration < 500; iteration++)
        {
            var onHandAvailable = (decimal)random.Next(-500, 5_000);
            var demands = Enumerable.Range(0, random.Next(0, 6))
                .Select(_ => new MaterialDemand(Today.AddDays(random.Next(-5, 15)), random.Next(0, 2_000)))
                .ToArray();
            var supplies = Enumerable.Range(0, random.Next(0, 6))
                .Select(_ => new IncomingSupply(Today.AddDays(random.Next(-5, 15)), random.Next(0, 2_000)))
                .ToArray();
            var because = $"iteration {iteration}, onHand {onHandAvailable}, {demands.Length} demands, {supplies.Length} supplies";

            var timeline = MaterialAvailabilityRules.BuildTimeline(onHandAvailable, demands, supplies, Today);
            var evaluation = MaterialAvailabilityRules.Evaluate(onHandAvailable, timeline);

            for (var i = 1; i < timeline.Count; i++)
            {
                Assert.True(timeline[i].RequiredDate > timeline[i - 1].RequiredDate, $"Timeline dates must strictly ascend - {because}");
                Assert.True(timeline[i].CumulativeRequired >= timeline[i - 1].CumulativeRequired, $"Cumulative demand cannot fall - {because}");
                Assert.True(timeline[i].CumulativeIncoming >= timeline[i - 1].CumulativeIncoming, $"Cumulative incoming cannot fall as the window widens - {because}");
            }

            foreach (var point in timeline)
            {
                Assert.Equal(onHandAvailable + point.CumulativeIncoming, point.AvailableByDate);
            }

            var expectedShortage = timeline.Count == 0
                ? 0m
                : timeline.Max(x => Math.Max(x.CumulativeRequired - x.AvailableByDate, 0m));
            Assert.Equal(expectedShortage, evaluation.ShortageQuantity);
            Assert.True(evaluation.ShortageQuantity >= 0, $"Shortage is never negative - {because}");
            Assert.Equal(evaluation.ShortageQuantity > 0, evaluation.MaterialRequiredDate is not null);

            if (evaluation.MaterialRequiredDate is { } shortfallDate)
            {
                // The first date that runs short cannot follow the date of the largest deficit: the
                // largest deficit is itself positive, so a shortfall exists at or before it.
                Assert.True(shortfallDate <= evaluation.EvaluationDate, $"First shortfall must not follow the evaluation date - {because}");
            }

            if (timeline.Count == 0)
            {
                Assert.Null(evaluation.EvaluationDate);
                Assert.Equal(onHandAvailable, evaluation.AvailableByDate);
            }
        }
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
