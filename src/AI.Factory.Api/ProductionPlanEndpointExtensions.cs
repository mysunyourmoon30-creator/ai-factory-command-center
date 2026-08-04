using AI.Factory.Core.MasterData;
using AI.Factory.Core.Production;
using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AI.Factory.Api;

public static class ProductionPlanEndpointExtensions
{
    public static IEndpointRouteBuilder MapProductionPlanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var plans = endpoints.MapGroup("/api/production-plans").RequireAuthorization();
        plans.MapGet("/", async (IProductionPlanService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)));
        plans.MapGet("/machines", async (IProductionPlanService service, CancellationToken ct) => Results.Ok(await service.ListMachinesAsync(ct)));
        plans.MapGet("/{id:long}", async (long id, IProductionPlanService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } plan ? Results.Ok(plan) : Results.NotFound());
        plans.MapPost("/", (HttpContext http, IAntiforgery anti, [FromBody] CreateProductionPlanCommand command, IProductionPlanService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, async () => (ProductionPlanDto?)await service.CreateAsync(command, http.User, ct)))
            .RequireAuthorization(PolicyNames.CanManageProductionPlans);
        plans.MapPost("/{id:long}/lifecycle", (long id, HttpContext http, IAntiforgery anti, [FromBody] TransitionProductionPlanCommand command, IProductionPlanService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, () => service.TransitionAsync(id, command, http.User, ct)))
            .RequireAuthorization(PolicyNames.CanManageProductionPlans);
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
