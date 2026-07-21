using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

public sealed class BrowserSessionOptions
{
    public string AccessCookieName { get; init; } = "zumbo-access";
    public string RefreshCookieName { get; init; } = "zumbo-refresh";
    public string CsrfCookieName { get; init; } = "zumbo-csrf";
    public string CsrfHeaderName { get; init; } = "X-CSRF-Token";
    public bool SecureCookies { get; init; } = true;
    public int RefreshCookieDays { get; init; } = 14;
}

public sealed record BrowserSessionResponse(
    UserProfileResponse User,
    DateTimeOffset ExpiresAt,
    string CsrfToken);

public sealed record BrowserLogoutRequest(bool AllSessions = false);

public sealed class BrowserSessionService(
    IdentityService identity,
    IUserRepository users,
    ICurrentUser currentUser,
    IOptions<Zumbo.BuildingBlocks.Application.Security.JwtOptions> jwtOptions,
    IOptions<BrowserSessionOptions> options)
{
    public async Task<BrowserSessionResponse> LoginAsync(
        LoginRequest request,
        HttpContext http,
        CancellationToken cancellationToken) =>
        Issue(http, await identity.LoginAsync(request, cancellationToken));

    public async Task<BrowserSessionResponse> RegisterAsync(
        RegisterUserRequest request,
        HttpContext http,
        CancellationToken cancellationToken) =>
        Issue(http, await identity.RegisterAsync(request, cancellationToken));

    public async Task<BrowserSessionResponse> RefreshAsync(
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var refreshToken = ReadRequiredRefreshCookie(http);
        return Issue(http, await identity.RefreshAsync(new RefreshTokenRequest(refreshToken), cancellationToken));
    }

    public async Task<LogoutResponse> LogoutAsync(
        BrowserLogoutRequest request,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var refreshToken = http.Request.Cookies[options.Value.RefreshCookieName];
        var result = string.IsNullOrWhiteSpace(refreshToken)
            ? new LogoutResponse(true, 0)
            : await identity.LogoutAsync(new LogoutRequest(refreshToken, request.AllSessions), cancellationToken);
        DeleteCookies(http);
        return result;
    }

    public async Task<BrowserSessionResponse> GetSessionAsync(
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
        var csrfToken = RotateCsrfCookie(http);
        return new BrowserSessionResponse(
            IdentityMappings.ToProfile(user),
            DateTimeOffset.UtcNow.AddMinutes(jwtOptions.Value.AccessTokenMinutes),
            csrfToken);
    }

    private BrowserSessionResponse Issue(HttpContext http, AuthResponse auth)
    {
        var settings = options.Value;
        http.Response.Cookies.Append(
            settings.AccessCookieName,
            auth.AccessToken,
            CookieOptions(auth.ExpiresAt, httpOnly: true));
        http.Response.Cookies.Append(
            settings.RefreshCookieName,
            auth.RefreshToken,
            CookieOptions(DateTimeOffset.UtcNow.AddDays(settings.RefreshCookieDays), httpOnly: true));
        var csrfToken = RotateCsrfCookie(http);
        return new BrowserSessionResponse(auth.User, auth.ExpiresAt, csrfToken);
    }

    private string RotateCsrfCookie(HttpContext http)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var settings = options.Value;
        http.Response.Cookies.Append(
            settings.CsrfCookieName,
            token,
            CookieOptions(DateTimeOffset.UtcNow.AddDays(settings.RefreshCookieDays), httpOnly: false));
        return token;
    }

    private string ReadRequiredRefreshCookie(HttpContext http) =>
        http.Request.Cookies[options.Value.RefreshCookieName]
        ?? throw new UnauthorizedException("Browser refresh session is missing.");

    private void DeleteCookies(HttpContext http)
    {
        var settings = options.Value;
        var deletion = CookieOptions(DateTimeOffset.UnixEpoch, httpOnly: true);
        http.Response.Cookies.Delete(settings.AccessCookieName, deletion);
        http.Response.Cookies.Delete(settings.RefreshCookieName, deletion);
        http.Response.Cookies.Delete(
            settings.CsrfCookieName,
            CookieOptions(DateTimeOffset.UnixEpoch, httpOnly: false));
    }

    private CookieOptions CookieOptions(DateTimeOffset expires, bool httpOnly) => new()
    {
        HttpOnly = httpOnly,
        Secure = options.Value.SecureCookies,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
        IsEssential = true
    };
}

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
