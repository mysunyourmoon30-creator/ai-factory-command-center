using AI.Factory.Core.Domain;
using AI.Factory.Core.Production;

namespace AI.Factory.UnitTests;

public sealed class ProcurementRuleTests
{
    [Fact]
    public void No_quantity_received_is_open() =>
        Assert.Equal(IncomingPurchaseOrderStatus.Open, ProcurementRules.CalculateStatus([(500m, 0m)]));

    [Fact]
    public void Some_quantity_received_on_any_item_is_partial() =>
        Assert.Equal(IncomingPurchaseOrderStatus.Partial, ProcurementRules.CalculateStatus([(500m, 200m), (300m, 0m)]));

    [Fact]
    public void Every_item_fully_received_is_received() =>
        Assert.Equal(IncomingPurchaseOrderStatus.Received, ProcurementRules.CalculateStatus([(500m, 500m), (300m, 300m)]));

    [Fact]
    public void One_item_short_of_full_keeps_the_order_partial_even_when_another_item_is_complete() =>
        Assert.Equal(IncomingPurchaseOrderStatus.Partial, ProcurementRules.CalculateStatus([(500m, 500m), (300m, 299m)]));
}
