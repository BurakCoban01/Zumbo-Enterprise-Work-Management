using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Teams;

internal static class TeamInviteTokenSecurity
{
    internal static string Create() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    internal static string Hash(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(token)))).ToLowerInvariant();

    internal static bool Matches(string? storedHash, string token)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var candidate = Hash(token);
        return storedHash.Length == candidate.Length
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(storedHash),
                Encoding.ASCII.GetBytes(candidate));
    }

    private static string Normalize(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new Zumbo.SharedKernel.ValidationException("Team invite token is required.");
        }

        var normalized = token.Trim();
        if (normalized.Length is < 32 or > 256)
        {
            throw new Zumbo.SharedKernel.ValidationException("Team invite token is invalid.");
        }

        return normalized;
    }
}
