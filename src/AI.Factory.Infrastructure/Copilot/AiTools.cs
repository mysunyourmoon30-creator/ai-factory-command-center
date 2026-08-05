using AI.Factory.Core.Copilot;
using AI.Factory.Core.Production;
using AI.Factory.Core.Reporting;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Copilot;

public sealed record MaterialShortageInfo(string RawMaterialCode, string RawMaterialName, decimal RequiredQuantity, decimal ProjectedAvailable, decimal ShortageQuantity, DateTime? MaterialRequiredDate);
public sealed record LatePurchaseOrderInfo(string PurchaseOrderNumber, DateTime ExpectedDate, int DelayDays, string OrderNumber, IReadOnlyCollection<string> RawMaterialCodes);

/// <summary>GetMaterialShortages() — wraps the already-tested Day 7 service; no new business logic.</summary>
public sealed class MaterialShortageAiTool(IMaterialShortageService shortages) : IAiTool
{
    public string Name => "GetMaterialShortages";
    public string Purpose => "Lists raw materials with an outstanding shortage.";
    public IReadOnlyCollection<string> AllowedRoles => RoleNames.All;
    public int MaxRecords => 20;

    public bool Matches(string question) =>
        CopilotKeywords.ContainsAny(question, "shortage", "shortages", "short", "material", "materials", "stock", "raw material");

    public async Task<object> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var rows = await shortages.ListAsync(cancellationToken);
        return rows.Where(x => x.ShortageQuantity > 0).Take(MaxRecords)
            .Select(x => new MaterialShortageInfo(x.RawMaterialCode, x.RawMaterialName, x.TotalRequirement, x.ProjectedAvailable, x.ShortageQuantity, x.MaterialRequiredDate))
            .ToArray();
    }
}

/// <summary>GetDelayedProductionOrders() — wraps the already-tested Day 9 Dashboard critical-risk query.</summary>
public sealed class DelayedProductionOrderAiTool(IDashboardService dashboard) : IAiTool
{
    public string Name => "GetDelayedProductionOrders";
    public string Purpose => "Lists customer orders at Critical delay risk.";
    public IReadOnlyCollection<string> AllowedRoles => RoleNames.All;
    public int MaxRecords => 20;

    public bool Matches(string question) =>
        CopilotKeywords.ContainsAny(question, "delay", "delayed", "late order", "late orders", "production order", "risk", "critical order");

    public async Task<object> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var risks = await dashboard.GetCriticalRisksAsync(cancellationToken);
        return risks.DelayedOrders.Take(MaxRecords).ToArray();
    }
}

/// <summary>GetLatePurchaseOrders() — one row per late Incoming PO, joined back to its source order.</summary>
public sealed class LatePurchaseOrderAiTool(AppDbContext dbContext, TimeProvider timeProvider) : IAiTool
{
    public string Name => "GetLatePurchaseOrders";
    public string Purpose => "Lists incoming purchase orders that are past their expected date.";
    public IReadOnlyCollection<string> AllowedRoles => RoleNames.All;
    public int MaxRecords => 20;

    public bool Matches(string question) =>
        CopilotKeywords.ContainsAny(question, "purchase order", "purchase orders", "po ", "late po", "supplier", "incoming");

    public async Task<object> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var today = timeProvider.GetUtcNow().UtcDateTime;
        var orders = await dbContext.IncomingPurchaseOrders.AsNoTracking()
            .Include(x => x.PurchaseRequest).ThenInclude(x => x.SourceProductionPlan).ThenInclude(x => x.CustomerOrder)
            .Include(x => x.Items).ThenInclude(x => x.RawMaterial)
            .ToArrayAsync(cancellationToken);

        return orders
            .Select(x => new
            {
                Order = x,
                Outstanding = x.Items.Sum(i => i.OrderedQuantity - i.ReceivedQuantity)
            })
            .Where(x => PurchaseRequestRules.IsLate(x.Order.Status, x.Order.ExpectedDate, today, x.Outstanding))
            .OrderBy(x => x.Order.ExpectedDate)
            .Take(MaxRecords)
            .Select(x => new LatePurchaseOrderInfo(
                x.Order.PurchaseOrderNumber,
                x.Order.ExpectedDate,
                PurchaseRequestRules.CalculateDelayDays(x.Order.ExpectedDate, today),
                x.Order.PurchaseRequest.SourceProductionPlan.CustomerOrder.OrderNumber,
                x.Order.Items.Select(i => i.RawMaterial.Code).ToArray()))
            .ToArray();
    }
}

/// <summary>GetDailyFactorySummary() — exact reuse of the already-tested Day 9 Daily Summary.</summary>
public sealed class DailyFactorySummaryAiTool(IDashboardService dashboard) : IAiTool
{
    public string Name => "GetDailyFactorySummary";
    public string Purpose => "Returns the factory's daily KPI snapshot.";
    public IReadOnlyCollection<string> AllowedRoles => RoleNames.All;
    public int MaxRecords => 1;

    public bool Matches(string question) =>
        CopilotKeywords.ContainsAny(question, "summary", "daily factory", "factory summary", "overview", "how is the factory", "factory status", "factory doing");

    public async Task<object> ExecuteAsync(CancellationToken cancellationToken = default) =>
        await dashboard.GetDailySummaryAsync(cancellationToken);
}

internal static class CopilotKeywords
{
    internal static bool ContainsAny(string question, params string[] keywords) =>
        keywords.Any(keyword => question.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
