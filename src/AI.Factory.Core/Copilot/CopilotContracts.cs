using System.Security.Claims;
using AI.Factory.Core.Domain;

namespace AI.Factory.Core.Copilot;

public sealed record AskCopilotCommand(string Question);

/// <summary>Matches the locked structured-output shape (Master Scope V4 §10.4).</summary>
public sealed record CopilotResponseDto(
    string Summary,
    RiskStatus? RiskLevel,
    IReadOnlyCollection<string> AffectedOrders,
    IReadOnlyCollection<string> RecommendedActions,
    bool IsFallback);

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
