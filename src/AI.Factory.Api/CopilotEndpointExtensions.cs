using AI.Factory.Core.Copilot;
using AI.Factory.Core.MasterData;
using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AI.Factory.Api;

public static class CopilotEndpointExtensions
{
    public static IEndpointRouteBuilder MapCopilotEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var copilot = endpoints.MapGroup("/api/ai-copilot").RequireAuthorization(PolicyNames.CanUseAiCopilot);
        copilot.MapPost("/ask", (HttpContext http, IAntiforgery anti, [FromBody] AskCopilotCommand command, ICopilotService service, CancellationToken ct) =>
            ExecuteAsync(http, anti, () => service.AskAsync(command, http.User, ct)))
            .RequireRateLimiting("ai-copilot");

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(HttpContext http, IAntiforgery antiforgery, Func<Task<CopilotResponseDto>> action)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(http);
            return Results.Ok(await action());
        }
        catch (DomainValidationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["validation"] = [exception.Message] });
        }
    }
}
