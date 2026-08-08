using AI.Factory.Core.MasterData;
using AI.Factory.Core.Production;

namespace AI.Factory.Web.Components.Shared;

/// <summary>
/// The set of exceptions a page is expected to show the operator rather than let escape.
///
/// <para>
/// <see cref="UnauthorizedAccessException" /> is the one that was missing everywhere. The API path
/// deliberately does not catch it - the named policy on the endpoint has already returned 403 by
/// then, so it can only mean a bug. The Blazor path has no such policy: components call the
/// application services in-process, so the service's own <c>EnsureCanManage</c> throw is the only
/// signal that this actor may not do this, and nothing was catching it. A role revoked mid-session
/// therefore tore the circuit down with a generic error instead of saying why.
/// </para>
///
/// <para>
/// Kept as a predicate rather than a base exception type so the API's exception-to-status mapping
/// (which must keep treating these four differently) is unaffected.
/// </para>
/// </summary>
public static class UserFacingError
{
    public static bool IsExpected(Exception exception) =>
        exception is DomainValidationException
            or BusinessConflictException
            or ConcurrencyConflictException
            or UnauthorizedAccessException;
}
