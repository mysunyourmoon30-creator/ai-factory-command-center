using AI.Factory.Core.MasterData;
using AI.Factory.Core.Machines;
using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AI.Factory.Api;

public static class MachineEndpointExtensions
{
    public static IEndpointRouteBuilder MapMachineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var machines = endpoints.MapGroup("/api/machines").RequireAuthorization();
        machines.MapGet("/", async (IMachineService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)));
        machines.MapPost("/{id:long}/simulate", (long id, HttpContext http, IAntiforgery anti, [FromBody] SimulateMachineUpdateCommand command, IMachineService service, CancellationToken ct) =>
            ExecuteAsync(http, anti, () => service.SimulateUpdateAsync(id, command, http.User, ct)))
            .RequireAuthorization(PolicyNames.CanUpdateMachineSimulator);

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(HttpContext http, IAntiforgery antiforgery, Func<Task<MachineDto?>> action)
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
