using System.Security.Claims;
using AI.Factory.Core.Domain;

namespace AI.Factory.Core.Copilot;

public sealed record AskCopilotCommand(string Question);

/// <summary>
/// Matches the locked structured-output shape (Master Scope V4 §10.4).
/// </summary>
/// <param name="ToolName">
/// Which allow-listed read-only tool produced the data the answer was grounded in, so the screen
/// can name its source instead of presenting model prose as if it came from nowhere.
/// <para>
/// Populated by the orchestrator from the tool it selected, never by the model:
/// <c>CopilotResponseValidator</c> does not read these two properties out of the response JSON at
/// all, so there is no path by which generated text could claim a source it did not use. Null on
/// the no-match and fallback paths, where no tool ran.
/// </para>
/// </param>
/// <param name="ToolPurpose">The same tool's declared purpose, which is what a reader can act on.</param>
public sealed record CopilotResponseDto(
    string Summary,
    RiskStatus? RiskLevel,
    IReadOnlyCollection<string> AffectedOrders,
    IReadOnlyCollection<string> RecommendedActions,
    bool IsFallback,
    string? ToolName = null,
    string? ToolPurpose = null);

public interface ICopilotService
{
    Task<CopilotResponseDto> AskAsync(AskCopilotCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
}

/// <summary>
/// One of the 4 allow-listed, read-only tools (Master Scope V4 §10.5). Every field named in the
/// spec is represented explicitly: Name, Purpose, AllowedRoles, MaxRecords are declared up
/// front so the orchestrator can enforce them before ExecuteAsync ever runs.
/// </summary>
public interface IAiTool
{
    string Name { get; }
    string Purpose { get; }
    IReadOnlyCollection<string> AllowedRoles { get; }
    int MaxRecords { get; }

    /// <summary>Matches the question against this tool's topic without calling any external service.</summary>
    bool Matches(string question);

    /// <summary>Read-only; returns a small object that will be JSON-serialized as model context.</summary>
    Task<object> ExecuteAsync(CancellationToken cancellationToken = default);
}

public interface IOllamaClient
{
    Task<string> CompleteAsync(string systemPrompt, string userContent, CancellationToken cancellationToken = default);
    Task<bool> IsReachableAsync(CancellationToken cancellationToken = default);
}

/// <summary>Backs /health/ready: Application, SQL Server, and Ollama (§10.11).</summary>
public sealed record ReadinessDto(bool Healthy, string Application, string Sql, string Ollama);

public interface IReadinessService
{
    Task<ReadinessDto> CheckAsync(CancellationToken cancellationToken = default);
}
