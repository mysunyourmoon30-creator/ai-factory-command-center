using System.Security.Claims;
using AI.Factory.Core.Domain;
using AI.Factory.Core.MasterData;
using AI.Factory.Core.Orders;
using AI.Factory.Core.Production;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Orders;

public sealed class CustomerOrderService(
    AppDbContext dbContext,
    IAuditWriter auditWriter,
    IOrderRiskCalculator riskCalculator,
    TimeProvider timeProvider) : ICustomerOrderService
{
    private static readonly HashSet<string> Priorities = new(StringComparer.OrdinalIgnoreCase) { "Normal", "High", "Urgent" };

    public async Task<CustomerOrderPage> ListAsync(CustomerOrderQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var orders = Query();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            orders = orders.Where(x => x.OrderNumber.Contains(search) || x.CustomerName.Contains(search));
        }
        if (query.LifecycleStatus is not null)
        {
            orders = orders.Where(x => x.Status == query.LifecycleStatus);
        }

        var totalCount = await orders.CountAsync(cancellationToken);
        var entities = await orders.OrderBy(x => x.DeliveryDate).ThenBy(x => x.OrderNumber)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new CustomerOrderPage(entities.Select(Map).ToArray(), page, pageSize, totalCount);
    }

    public async Task<CustomerOrderDto?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await Query().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<CustomerOrderDto> CreateAsync(CreateCustomerOrderCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanManage(actor);
        await ValidateAsync(command.OrderNumber, command.CustomerName, command.FormulationId, command.Quantity, command.DeliveryDate, command.Priority, null, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new CustomerOrder
        {
            OrderNumber = command.OrderNumber.Trim(),
            CustomerName = command.CustomerName.Trim(),
            FormulationId = command.FormulationId,
            Quantity = command.Quantity,
            DeliveryDate = NormalizeDate(command.DeliveryDate),
            Priority = NormalizePriority(command.Priority),
            Status = CustomerOrderStatus.Draft,
            CreatedByUserId = CurrentUserId(actor),
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.CustomerOrders.Add(entity);
        await SaveWithConcurrencyAsync(cancellationToken);
        await auditWriter.WriteAsync("Create Customer Order", nameof(CustomerOrder), entity.Id, "Success", cancellationToken: cancellationToken);
        return (await GetAsync(entity.Id, cancellationToken))!;
    }

    public async Task<CustomerOrderDto?> UpdateAsync(long id, UpdateCustomerOrderCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanManage(actor);
        await ValidateAsync(command.OrderNumber, command.CustomerName, command.FormulationId, command.Quantity, command.DeliveryDate, command.Priority, id, cancellationToken);
        var entity = await dbContext.CustomerOrders.Include(x => x.ProductionPlan).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return null;
        EnsureRowVersion(entity, command.RowVersion);
        if (entity.Status != CustomerOrderStatus.Draft || entity.ProductionPlan is not null)
            throw new DomainValidationException("Only a Draft customer order without a production plan can be updated.");

        dbContext.Entry(entity).Property(x => x.RowVersion).OriginalValue = command.RowVersion;
        entity.OrderNumber = command.OrderNumber.Trim();
        entity.CustomerName = command.CustomerName.Trim();
        entity.FormulationId = command.FormulationId;
        entity.Quantity = command.Quantity;
        entity.DeliveryDate = NormalizeDate(command.DeliveryDate);
        entity.Priority = NormalizePriority(command.Priority);
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await SaveWithConcurrencyAsync(cancellationToken);
        await auditWriter.WriteAsync("Update Customer Order", nameof(CustomerOrder), entity.Id, "Success", cancellationToken: cancellationToken);
        return await GetAsync(entity.Id, cancellationToken);
    }

    public async Task<CustomerOrderDto?> TransitionAsync(long id, TransitionCustomerOrderCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanManage(actor);
        var entity = await dbContext.CustomerOrders.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return null;
        EnsureRowVersion(entity, command.RowVersion);
        var expected = entity.Status switch
        {
            CustomerOrderStatus.Draft => CustomerOrderStatus.Planned,
            CustomerOrderStatus.Planned => CustomerOrderStatus.InProduction,
            CustomerOrderStatus.InProduction => CustomerOrderStatus.Completed,
            _ => (CustomerOrderStatus?)null
        };
        if (expected != command.TargetStatus)
            throw new DomainValidationException($"Invalid lifecycle transition from {entity.Status} to {command.TargetStatus}.");

        dbContext.Entry(entity).Property(x => x.RowVersion).OriginalValue = command.RowVersion;
        entity.Status = command.TargetStatus;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await SaveWithConcurrencyAsync(cancellationToken);
        await auditWriter.WriteAsync("Transition Customer Order", nameof(CustomerOrder), entity.Id, $"Success: {entity.Status}", cancellationToken: cancellationToken);
        return await GetAsync(entity.Id, cancellationToken);
    }

    private IQueryable<CustomerOrder> Query() => dbContext.CustomerOrders.AsNoTracking()
        .Include(x => x.Formulation).Include(x => x.ProductionPlan);

    private CustomerOrderDto Map(CustomerOrder x) => new(
        x.Id, x.OrderNumber, x.CustomerName, x.FormulationId, x.Formulation.Code, x.Quantity,
        x.DeliveryDate, x.Priority, x.Status,
        riskCalculator.Calculate(x.DeliveryDate, x.ProductionPlan?.PlannedCompletionDate),
        x.ProductionPlan is not null, x.RowVersion);

    private async Task ValidateAsync(string orderNumber, string customerName, long formulationId, decimal quantity, DateTime deliveryDate, string priority, long? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderNumber) || orderNumber.Trim().Length > 30)
            throw new DomainValidationException("Order number is required and limited to 30 characters.");
        if (string.IsNullOrWhiteSpace(customerName) || customerName.Trim().Length > 150)
            throw new DomainValidationException("Customer name is required and limited to 150 characters.");
        if (quantity <= 0) throw new DomainValidationException("Quantity must be greater than zero.");
        if (deliveryDate == default) throw new DomainValidationException("Delivery date is required.");
        if (string.IsNullOrWhiteSpace(priority) || !Priorities.Contains(priority.Trim()))
            throw new DomainValidationException("Priority must be Normal, High, or Urgent.");
        if (!await dbContext.Formulations.AnyAsync(x => x.Id == formulationId && x.IsActive, cancellationToken))
            throw new DomainValidationException("An active formulation is required.");
        if (await dbContext.CustomerOrders.AnyAsync(x => x.Id != id && x.OrderNumber == orderNumber.Trim(), cancellationToken))
            throw new DomainValidationException("Order number already exists.");
    }

    private async Task SaveWithConcurrencyAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException("Customer order changed; reload before saving."); }
        // ValidateAsync's duplicate check is read-then-write, so two callers submitting the same
        // order number concurrently both pass it and the unique index on OrderNumber settles the
        // race. Untranslated, the loser got a raw store exception - a 500 on the API and a dead
        // circuit in the UI - rather than the message the pre-check would have produced.
        // Must stay below the concurrency catch: DbUpdateConcurrencyException derives from this.
        catch (DbUpdateException) { throw new BusinessConflictException("Order number already exists."); }
    }

    private static void EnsureRowVersion(CustomerOrder entity, byte[] rowVersion)
    {
        if (rowVersion is null || !entity.RowVersion.SequenceEqual(rowVersion))
            throw new ConcurrencyConflictException("Customer order changed; reload before saving.");
    }

    private static void EnsureCanManage(ClaimsPrincipal actor)
    {
        if (!actor.IsInRole(RoleNames.Admin) && !actor.IsInRole(RoleNames.Planner))
            throw new UnauthorizedAccessException("Admin or Planner role is required to manage customer orders.");
    }

    private static long CurrentUserId(ClaimsPrincipal actor)
    {
        var value = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(value, out var id) ? id : throw new DomainValidationException("An authenticated user is required to create an order.");
    }

    private static DateTime NormalizeDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    private static string NormalizePriority(string value) => Priorities.Single(x => x.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
}
