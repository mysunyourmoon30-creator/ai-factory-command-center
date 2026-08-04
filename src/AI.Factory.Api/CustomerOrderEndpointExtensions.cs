using AI.Factory.Core.MasterData;
using AI.Factory.Core.Orders;
using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AI.Factory.Api;

public static class CustomerOrderEndpointExtensions
{
    public static IEndpointRouteBuilder MapCustomerOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var orders = endpoints.MapGroup("/api/customer-orders").RequireAuthorization();
        orders.MapGet("/", async (string? search, AI.Factory.Core.Domain.CustomerOrderStatus? lifecycleStatus, int? page, int? pageSize, ICustomerOrderService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(new(search, lifecycleStatus, page ?? 1, pageSize ?? 20), ct)));
        orders.MapGet("/{id:long}", async (long id, ICustomerOrderService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } item ? Results.Ok(item) : Results.NotFound());
        orders.MapPost("/", (HttpContext http, IAntiforgery anti, [FromBody] CreateCustomerOrderCommand command, ICustomerOrderService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, async () => (CustomerOrderDto?)await service.CreateAsync(command, http.User, ct))).RequireAuthorization(PolicyNames.CanManageOrders);
        orders.MapPut("/{id:long}", (long id, HttpContext http, IAntiforgery anti, [FromBody] UpdateCustomerOrderCommand command, ICustomerOrderService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, () => service.UpdateAsync(id, command, http.User, ct))).RequireAuthorization(PolicyNames.CanManageOrders);
        orders.MapPost("/{id:long}/lifecycle", (long id, HttpContext http, IAntiforgery anti, [FromBody] TransitionCustomerOrderCommand command, ICustomerOrderService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, () => service.TransitionAsync(id, command, http.User, ct))).RequireAuthorization(PolicyNames.CanManageOrders);
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
        catch (ConcurrencyConflictException exception) { return Results.Conflict(new { error = exception.Message }); }
    }
}
