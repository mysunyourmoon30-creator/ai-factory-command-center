using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AI.Factory.Api;

public static class EndpointRegistrationExtensions
{
    public static IEndpointRouteBuilder MapAiFactoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
            .AllowAnonymous();

        return endpoints;
    }
}
