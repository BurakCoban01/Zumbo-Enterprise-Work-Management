namespace Zumbo.BuildingBlocks.Application.Security;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "Zumbo";
    public string Audience { get; init; } = "Zumbo.Clients";
    public string SigningKey { get; init; } = string.Empty;
    public string ActiveKeyId { get; init; } = "legacy";
    public IReadOnlyDictionary<string, string> SigningKeys { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public int AccessTokenMinutes { get; init; } = 30;

    public IReadOnlyDictionary<string, string> ResolveSigningKeys()
    {
        if (SigningKeys.Count > 0)
        {
            if (SigningKeys.Any(x =>
                    string.IsNullOrWhiteSpace(x.Key)
                    || x.Key.Trim().Length > 128
                    || x.Key.Any(char.IsControl)
                    || string.IsNullOrWhiteSpace(x.Value)
                    || x.Value.Length < 32))
            {
                throw new InvalidOperationException(
                    "Every JWT signing key requires a valid id and at least 32 characters of key material.");
            }

            return SigningKeys.ToDictionary(x => x.Key.Trim(), x => x.Value, StringComparer.Ordinal);
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [string.IsNullOrWhiteSpace(ActiveKeyId) ? "legacy" : ActiveKeyId.Trim()] = SigningKey
        };
    }

    public KeyValuePair<string, string> ResolveActiveSigningKey()
    {
        var keys = ResolveSigningKeys();
        var activeKeyId = string.IsNullOrWhiteSpace(ActiveKeyId) ? "legacy" : ActiveKeyId.Trim();
        if (!keys.TryGetValue(activeKeyId, out var key) || key.Length < 32)
        {
            throw new InvalidOperationException("JWT active signing key must exist and contain at least 32 characters.");
        }

        return new KeyValuePair<string, string>(activeKeyId, key);
    }
}
