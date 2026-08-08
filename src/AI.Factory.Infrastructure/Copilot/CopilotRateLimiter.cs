using System.Collections.Concurrent;

namespace AI.Factory.Infrastructure.Copilot;

/// <summary>
/// Per-user question budget for the Copilot.
///
/// <para>
/// The <c>"ai-copilot"</c> rate-limiting policy only guards <c>POST /api/ai-copilot/ask</c>. The
/// Blazor page calls <c>ICopilotService</c> in-process — that is this codebase's shared-service
/// rule, not an oversight — so the endpoint policy never runs on the path the UI actually takes,
/// and every question asked through the screen was unlimited. The same shape of gap as finding A2
/// on <c>IAdminUserService</c>: the protection sat on a path the UI does not use.
/// </para>
///
/// <para>
/// Deliberately the same fixed-window shape as the endpoint policy it mirrors, reading the same
/// <c>RateLimits:AiCopilotPermitLimit</c> configuration key so the two budgets cannot drift apart.
/// Partitioned by user rather than by IP, because in-process there is no connection to partition
/// on and the user is the more meaningful subject anyway.
/// </para>
///
/// <para>
/// One dictionary entry per authenticated user, so growth is bounded by the user table rather than
/// by traffic.
/// </para>
/// </summary>
public sealed class CopilotRateLimiter(TimeProvider timeProvider, int permitLimit)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<long, Counter> counters = new();

    /// <param name="Permitted">False once the caller has spent its budget for the current window.</param>
    /// <param name="IsFirstRejection">
    /// True only for the first rejected question in a window. Callers log on this and stay silent
    /// afterwards, so a flood cannot turn itself into one audit write per request.
    /// </param>
    public sealed record Decision(bool Permitted, bool IsFirstRejection);

    public Decision Check(long userId)
    {
        var now = timeProvider.GetUtcNow();
        var counter = counters.GetOrAdd(userId, _ => new Counter());

        lock (counter)
        {
            if (now - counter.WindowStart >= Window)
            {
                counter.WindowStart = now;
                counter.Count = 0;
            }

            counter.Count++;
            return counter.Count <= permitLimit
                ? new Decision(true, false)
                : new Decision(false, counter.Count == permitLimit + 1);
        }
    }

    private sealed class Counter
    {
        public DateTimeOffset WindowStart;
        public int Count;
    }
}
