using AI.Factory.Core.MasterData;
using AI.Factory.Core.Production;
using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AI.Factory.Api;

public static class ProcurementEndpointExtensions
{
    public static IEndpointRouteBuilder MapProcurementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var requests = endpoints.MapGroup("/api/purchase-requests").RequireAuthorization();
        requests.MapGet("/", async (IProcurementService service, CancellationToken ct) => Results.Ok(await service.ListPurchaseRequestsAsync(ct)));
        requests.MapGet("/{id:long}", async (long id, IProcurementService service, CancellationToken ct) =>
            await service.GetPurchaseRequestAsync(id, ct) is { } request ? Results.Ok(request) : Results.NotFound());
        requests.MapPost("/{id:long}/submit", (long id, HttpContext http, IAntiforgery anti, [FromBody] SubmitPurchaseRequestCommand command, IProcurementService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, () => service.SubmitPurchaseRequestAsync(id, command, http.User, ct)))
            .RequireAuthorization(PolicyNames.CanCreatePurchaseRequest);
        requests.MapPost("/{id:long}/approve", (long id, HttpContext http, IAntiforgery anti, [FromBody] ApprovePurchaseRequestCommand command, IProcurementService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, () => service.ApprovePurchaseRequestAsync(id, command, http.User, ct)))
            .RequireAuthorization(PolicyNames.CanApprovePurchaseRequest);
        requests.MapPost("/{id:long}/reject", (long id, HttpContext http, IAntiforgery anti, [FromBody] RejectPurchaseRequestCommand command, IProcurementService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, () => service.RejectPurchaseRequestAsync(id, command, http.User, ct)))
            .RequireAuthorization(PolicyNames.CanApprovePurchaseRequest);

        var incomingOrders = endpoints.MapGroup("/api/incoming-purchase-orders").RequireAuthorization();
        incomingOrders.MapGet("/", async (IProcurementService service, CancellationToken ct) => Results.Ok(await service.ListIncomingPurchaseOrdersAsync(ct)));
        incomingOrders.MapGet("/{id:long}", async (long id, IProcurementService service, CancellationToken ct) =>
            await service.GetIncomingPurchaseOrderAsync(id, ct) is { } order ? Results.Ok(order) : Results.NotFound());
        incomingOrders.MapPost("/", (HttpContext http, IAntiforgery anti, [FromBody] CreateIncomingPurchaseOrderCommand command, IProcurementService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, async () => (IncomingPurchaseOrderDto?)await service.CreateIncomingPurchaseOrderAsync(command, http.User, ct)))
            .RequireAuthorization(PolicyNames.CanRecordIncomingPurchaseOrder);
        incomingOrders.MapPost("/{id:long}/receive", (long id, HttpContext http, IAntiforgery anti, [FromBody] SetReceivedQuantityCommand command, IProcurementService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, () => service.SetReceivedQuantityAsync(id, command, http.User, ct)))
            .RequireAuthorization(PolicyNames.CanRecordIncomingPurchaseOrder);

        return endpoints;
    }

    private static async Task<IResult> ExecuteWriteAsync<T>(HttpContext http, IAntiforgery antiforgery, Func<Task<T?>> action)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(http);
            var result = await action();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (DomainValidationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["validation"] = [exception.Message] });
        }
        catch (BusinessConflictException exception) { return Results.Conflict(new { error = exception.Message }); }
        catch (ConcurrencyConflictException exception) { return Results.Conflict(new { error = exception.Message }); }
    }
}
