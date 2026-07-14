using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Zumbo.Modules.Identity;

public static class ZumboAuthenticationSchemes
{
    public const string Smart = "ZumboAuth";
    public const string ApiKey = "ApiKey";
}

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyService apiKeyService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var rawKey = Request.Headers["X-API-Key"].ToString();
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return AuthenticateResult.NoResult();
        }

        var principal = await apiKeyService.AuthenticateAsync(rawKey, Context.RequestAborted);
        if (principal is null || !principal.Scopes.Contains("api:full", StringComparer.Ordinal))
        {
            return AuthenticateResult.Fail("API key is invalid or expired.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, principal.UserId),
            new(ClaimTypes.Name, principal.Username),
            new(ClaimTypes.Email, principal.Email),
            new("organizationId", principal.OrganizationId),
            new("apiKeyId", principal.ApiKeyId)
        };
        claims.AddRange(principal.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(principal.Scopes.Select(scope => new Claim("scope", scope)));
        var identity = new ClaimsIdentity(claims, ZumboAuthenticationSchemes.ApiKey);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            ZumboAuthenticationSchemes.ApiKey);
        return AuthenticateResult.Success(ticket);
    }
}
