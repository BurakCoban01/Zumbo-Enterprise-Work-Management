using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Zumbo.BuildingBlocks.Infrastructure.Security;

public interface IPasswordHasher
{
    string Hash(string plainPassword);
    bool Verify(string plainPassword, string passwordHash);
}

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;

    public string Hash(string plainPassword)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            plainPassword,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        return $"PBKDF2-SHA256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string plainPassword, string passwordHash)
    {
        if (string.IsNullOrEmpty(plainPassword) || string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        try
        {
            var parts = passwordHash.Split('$');
            if (parts.Length != 4
                || parts[0] != "PBKDF2-SHA256"
                || !int.TryParse(parts[1], out var iterations)
                || iterations is < 10_000 or > 1_000_000)
            {
                return false;
            }

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length is < 8 or > 64 || expected.Length is < 16 or > 128)
            {
                return false;
            }
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                plainPassword,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "Zumbo";
    public string Audience { get; init; } = "Zumbo.Clients";
    public string SigningKey { get; init; } = "development-signing-key-change-me-please-32";
    public int AccessTokenMinutes { get; init; } = 30;
}

public sealed record TokenUser(
    string Id,
    string Username,
    string Email,
    string OrganizationId,
    IReadOnlyCollection<string> Roles,
    string SecurityStamp,
    string SessionId);

public interface ITokenIssuer
{
    string CreateAccessToken(TokenUser user, JwtOptions options, DateTimeOffset now);
    string CreateRefreshToken();
}

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

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
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
