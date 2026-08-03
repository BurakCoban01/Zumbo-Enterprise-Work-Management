using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

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
