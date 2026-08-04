using AI.Factory.Core.MasterData;
using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AI.Factory.Api;

public static class MasterDataEndpointExtensions
{
    public static IEndpointRouteBuilder MapMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var raw = endpoints.MapGroup("/api/raw-materials").RequireAuthorization();
        raw.MapGet("/", async (IMasterDataService service, CancellationToken ct) => Results.Ok(await service.ListRawMaterialsAsync(ct)));
        raw.MapGet("/{id:long}", async (long id, IMasterDataService service, CancellationToken ct) =>
            await service.GetRawMaterialAsync(id, ct) is { } item ? Results.Ok(item) : Results.NotFound());
        raw.MapPost("/", (HttpContext http, IAntiforgery anti, [FromBody] RawMaterialCommand command, IMasterDataService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, async () => (RawMaterialDto?)await service.CreateRawMaterialAsync(command, ct))).RequireAuthorization(PolicyNames.CanManageMasterData);
        raw.MapPut("/{id:long}", (long id, HttpContext http, IAntiforgery anti, [FromBody] RawMaterialCommand command, IMasterDataService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, () => service.UpdateRawMaterialAsync(id, command, ct))).RequireAuthorization(PolicyNames.CanManageMasterData);

        var formulations = endpoints.MapGroup("/api/formulations").RequireAuthorization();
        formulations.MapGet("/", async (IMasterDataService service, CancellationToken ct) => Results.Ok(await service.ListFormulationsAsync(ct)));
        formulations.MapGet("/{id:long}", async (long id, IMasterDataService service, CancellationToken ct) =>
            await service.GetFormulationAsync(id, ct) is { } item ? Results.Ok(item) : Results.NotFound());
        formulations.MapPost("/", (HttpContext http, IAntiforgery anti, [FromBody] FormulationCommand command, IMasterDataService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, async () => (FormulationDto?)await service.CreateFormulationAsync(command, ct))).RequireAuthorization(PolicyNames.CanManageMasterData);
        formulations.MapPut("/{id:long}", (long id, HttpContext http, IAntiforgery anti, [FromBody] FormulationCommand command, IMasterDataService service, CancellationToken ct) =>
            ExecuteWriteAsync(http, anti, () => service.UpdateFormulationAsync(id, command, ct))).RequireAuthorization(PolicyNames.CanManageMasterData);

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
        catch (DomainValidationException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["validation"] = [exception.Message] }); }
        catch (ConcurrencyConflictException exception) { return Results.Conflict(new { error = exception.Message }); }
    }
}
