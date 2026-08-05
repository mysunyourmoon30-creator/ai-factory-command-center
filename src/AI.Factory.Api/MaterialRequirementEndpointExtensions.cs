using AI.Factory.Core.Production;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AI.Factory.Api;

public static class MaterialRequirementEndpointExtensions
{
    public static IEndpointRouteBuilder MapMaterialRequirementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var requirements = endpoints.MapGroup("/api/material-requirements").RequireAuthorization();
        requirements.MapGet("/", async (IMaterialRequirementQueryService service, CancellationToken ct) =>
            Results.Ok(await service.ListAvailabilityAsync(ct)));
        requirements.MapGet("/production-plans/{productionPlanId:long}", async (long productionPlanId, IMaterialRequirementQueryService service, CancellationToken ct) =>
            await service.GetByProductionPlanAsync(productionPlanId, ct) is { } plan ? Results.Ok(plan) : Results.NotFound());
        return endpoints;
    }
}
