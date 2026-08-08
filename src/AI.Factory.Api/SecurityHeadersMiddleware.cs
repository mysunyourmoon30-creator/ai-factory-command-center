using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AI.Factory.Api;

/// <summary>
/// Writes the security response headers the app would otherwise ship without entirely
/// (finding A1 in docs/00_Project_Status.md's Screen x Role capability matrix). HSTS is
/// deliberately not set here - <c>UseHsts()</c> in Program.cs already owns it, and only
/// outside Development.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    // Blazor Web App with the Interactive Server render mode constrains what this policy can be:
    //
    // - script-src must allow inline. <ImportMap /> emits an inline <script type="importmap">,
    //   and SSR streaming rendering injects inline <script> blocks for each streamed update.
    //   A bare 'self' silently breaks both - the page still paints, but nothing is interactive.
    // - connect-src must allow ws:/wss: for the Interactive Server circuit's WebSocket.
    // - style-src must allow inline for Bootstrap and Blazor's own error UI.
    //
    // 'unsafe-inline' on script-src means this policy is NOT a meaningful XSS defence. It is
    // still worth setting: frame-ancestors, base-uri, object-src, form-action and default-src
    // all close real vectors that no other header in this app covers, and Razor's automatic
    // output encoding is what actually carries the XSS burden here.
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self' data:; " +
        "connect-src 'self' ws: wss:; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        // Written from OnStarting rather than inline, because Blazor's own endpoint appends a
        // `Content-Security-Policy: frame-ancestors 'self'` further down the pipeline. Setting
        // ours here first would leave the response carrying two CSP headers; browsers then
        // enforce the intersection, which still works but is needlessly confusing to audit.
        // OnStarting callbacks run last-registered-first, and this middleware registers first,
        // so this callback runs last and the indexer assignment replaces whatever Blazor set.
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;

            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            headers["X-Content-Type-Options"] = "nosniff";
            // Redundant with frame-ancestors for modern browsers, kept for older ones.
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
