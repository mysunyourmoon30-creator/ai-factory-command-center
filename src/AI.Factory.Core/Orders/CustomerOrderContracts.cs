using AI.Factory.Core.Domain;
using System.Security.Claims;

namespace AI.Factory.Core.Orders;

public sealed record CustomerOrderDto(
    long Id,
    string OrderNumber,
    string CustomerName,
    long FormulationId,
    string FormulationCode,
    decimal Quantity,
    DateTime DeliveryDate,
    string Priority,
    CustomerOrderStatus LifecycleStatus,
    RiskStatus RiskStatus,
    bool HasProductionPlan,
    byte[] RowVersion);

public sealed record CustomerOrderPage(IReadOnlyCollection<CustomerOrderDto> Items, int Page, int PageSize, int TotalCount);
public sealed record CustomerOrderQuery(string? Search = null, CustomerOrderStatus? LifecycleStatus = null, int Page = 1, int PageSize = 20);
public sealed record CreateCustomerOrderCommand(string OrderNumber, string CustomerName, long FormulationId, decimal Quantity, DateTime DeliveryDate, string Priority);
public sealed record UpdateCustomerOrderCommand(string OrderNumber, string CustomerName, long FormulationId, decimal Quantity, DateTime DeliveryDate, string Priority, byte[] RowVersion);
public sealed record TransitionCustomerOrderCommand(CustomerOrderStatus TargetStatus, byte[] RowVersion);

public interface ICustomerOrderService
{
    Task<CustomerOrderPage> ListAsync(CustomerOrderQuery query, CancellationToken cancellationToken = default);
    Task<CustomerOrderDto?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<CustomerOrderDto> CreateAsync(CreateCustomerOrderCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    Task<CustomerOrderDto?> UpdateAsync(long id, UpdateCustomerOrderCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    Task<CustomerOrderDto?> TransitionAsync(long id, TransitionCustomerOrderCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
}

public interface IOrderRiskCalculator
{
    RiskStatus Calculate(DateTime deliveryDate, DateTime? plannedCompletionDate);
}

public sealed class OrderRiskCalculator : IOrderRiskCalculator
{
    public RiskStatus Calculate(DateTime deliveryDate, DateTime? plannedCompletionDate)
    {
        if (plannedCompletionDate is null)
        {
            return RiskStatus.Normal;
        }

        var bufferDays = (deliveryDate.Date - plannedCompletionDate.Value.Date).Days;
        return bufferDays switch
        {
            < 0 => RiskStatus.Critical,
            <= 1 => RiskStatus.Warning,
            _ => RiskStatus.Normal
        };
    }
}
