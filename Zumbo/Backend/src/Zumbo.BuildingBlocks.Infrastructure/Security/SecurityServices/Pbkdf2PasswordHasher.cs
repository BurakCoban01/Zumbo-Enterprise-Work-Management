using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.BuildingBlocks.Infrastructure.Security;

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

    public bool NeedsRehash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return true;
        }

        try
        {
            var parts = passwordHash.Split('$');
            return parts.Length != 4
                || parts[0] != "PBKDF2-SHA256"
                || !int.TryParse(parts[1], out var iterations)
                || iterations != Iterations
                || Convert.FromBase64String(parts[2]).Length != SaltSize
                || Convert.FromBase64String(parts[3]).Length != KeySize;
        }
        catch (FormatException)
        {
            return true;
        }
    }
}
