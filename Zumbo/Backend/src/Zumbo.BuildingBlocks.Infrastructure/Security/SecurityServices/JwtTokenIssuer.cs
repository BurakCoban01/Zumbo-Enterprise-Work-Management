using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.BuildingBlocks.Infrastructure.Security;

public sealed class JwtTokenIssuer : ITokenIssuer
{
    public string CreateAccessToken(TokenUser user, JwtOptions options, DateTimeOffset now)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("organizationId", user.OrganizationId),
            new("securityStamp", user.SecurityStamp),
            new("sessionId", user.SessionId)
        };

        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var activeKey = options.ResolveActiveSigningKey();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(activeKey.Value))
        {
            KeyId = activeKey.Key
        };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            options.Issuer,
            options.Audience,
            claims,
            now.UtcDateTime,
            now.AddMinutes(options.AccessTokenMinutes).UtcDateTime,
            credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}
