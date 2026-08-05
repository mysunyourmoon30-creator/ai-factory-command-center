using AI.Factory.Core.MasterData;
using AI.Factory.Core.Production;
using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AI.Factory.Api;

public static class MaterialShortageEndpointExtensions
{
    public static IEndpointRouteBuilder MapMaterialShortageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var shortages = endpoints.MapGroup("/api/material-shortages").RequireAuthorization();
        shortages.MapGet("/", async (IMaterialShortageService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct)));
        shortages.MapGet("/{rawMaterialId:long}", async (long rawMaterialId, IMaterialShortageService service, CancellationToken ct) =>
            await service.GetAsync(rawMaterialId, ct) is { } shortage ? Results.Ok(shortage) : Results.NotFound());
        shortages.MapPost("/purchase-requests", (HttpContext http, IAntiforgery anti, [FromBody] CreatePurchaseRequestCommand command, IMaterialShortageService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, async () => (PurchaseRequestDto?)await service.CreatePurchaseRequestAsync(command, http.User, ct)))
            .RequireAuthorization(PolicyNames.CanCreatePurchaseRequest);
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
