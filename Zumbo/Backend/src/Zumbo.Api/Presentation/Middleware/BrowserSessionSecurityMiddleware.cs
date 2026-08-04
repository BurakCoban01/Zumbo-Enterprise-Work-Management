using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

public sealed class BrowserSessionSecurityMiddleware(
    RequestDelegate next,
    IOptions<BrowserSessionOptions> options,
    IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var isBrowserAuth = path.StartsWithSegments("/api/browser-auth");
        var hasBrowserCookie = context.Request.Cookies.ContainsKey(options.Value.AccessCookieName)
            || context.Request.Cookies.ContainsKey(options.Value.RefreshCookieName);
        var hasBearerHeader = context.Request.Headers.Authorization
            .ToString()
            .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

        if (isBrowserAuth || hasBrowserCookie && !hasBearerHeader)
        {
            EnsureAllowedOrigin(context);
        }

        var requiresCsrf = IsUnsafe(context.Request.Method)
            && (!hasBearerHeader && hasBrowserCookie
                || path.StartsWithSegments("/api/browser-auth/refresh")
                || path.StartsWithSegments("/api/browser-auth/logout"));
        if (requiresCsrf)
        {
            EnsureCsrf(context);
        }

        await next(context);
    }

    private void EnsureAllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var candidate))
        {
            throw new ForbiddenException("Browser session requests require an allowed Origin header.");
        }

        var requestOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
        var allowed = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (!origin.Equals(requestOrigin, StringComparison.OrdinalIgnoreCase)
            && !allowed.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Browser session request origin is not allowed.");
        }

        if (candidate.UserInfo.Length > 0)
        {
            throw new ForbiddenException("Browser session request origin is invalid.");
        }
    }

    private void EnsureCsrf(HttpContext context)
    {
        var settings = options.Value;
        var cookie = context.Request.Cookies[settings.CsrfCookieName];
        var header = context.Request.Headers[settings.CsrfHeaderName].ToString();
        if (string.IsNullOrWhiteSpace(cookie)
            || string.IsNullOrWhiteSpace(header)
            || !FixedTimeEquals(cookie, header))
        {
            throw new ForbiddenException("CSRF token is missing or invalid.");
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(left));
        var rightHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(right));
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private static bool IsUnsafe(string method) =>
        !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);
}
