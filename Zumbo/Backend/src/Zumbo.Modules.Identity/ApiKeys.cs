using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed record CreateApiKeyRequest(
    string Name,
    string Password,
    string? MfaCode,
    DateTimeOffset? ExpiresAt,
    IReadOnlyCollection<string>? Scopes);
public sealed record CreatedApiKeyResponse(
    string Id,
    string Name,
    string Key,
    string KeyPrefix,
    IReadOnlyCollection<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
public sealed record ApiKeyResponse(
    string Id,
    string Name,
    string KeyPrefix,
    IReadOnlyCollection<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);
public sealed record ApiKeyPrincipal(
    string ApiKeyId,
    string UserId,
    string Username,
    string Email,
    string OrganizationId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Scopes);

public sealed class ApiKeyDocument : IDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class ApiKeyService(
    IDocumentRepository<ApiKeyDocument> apiKeys,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IMfaSecretProtector mfaSecretProtector,
    IIdentityAuditWriter audit,
    IClock clock,
    ICurrentUser currentUser)
{
    private static readonly string[] AllowedScopes = ["api:full"];

    public async Task<CreatedApiKeyResponse> CreateAsync(
        CreateApiKeyRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length is < 3 or > 80)
        {
            throw new ValidationException("API key name must be between 3 and 80 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Password)
            || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Password is invalid.");
        }

        if (user.MfaEnabled)
        {
            if (string.IsNullOrWhiteSpace(user.MfaSecretProtected)
                || !TotpSecurity.Verify(
                    mfaSecretProtector.Unprotect(user.MfaSecretProtected),
                    request.MfaCode,
                    clock.UtcNow))
            {
                throw new AuthenticationChallengeException("MFA_INVALID", "A valid TOTP code is required.");
            }
        }

        var scopes = (request.Scopes ?? AllowedScopes)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (scopes.Count == 0 || scopes.Except(AllowedScopes, StringComparer.Ordinal).Any())
        {
            throw new ValidationException("API key scopes are invalid.");
        }

        var now = clock.UtcNow;
        var expiresAt = request.ExpiresAt ?? now.AddDays(90);
        if (expiresAt <= now.AddHours(1) || expiresAt > now.AddDays(365))
        {
            throw new ValidationException("API key expiry must be between 1 hour and 365 days.");
        }

        var document = new ApiKeyDocument
        {
            UserId = user.Id,
            OrganizationId = user.OrganizationId,
            Name = request.Name.Trim(),
            Scopes = scopes,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        var rawKey = $"zmb_{document.Id}_{secret}";
        document.KeyPrefix = $"zmb_{document.Id[..8]}";
        document.KeyHash = Hash(rawKey);
        await apiKeys.CreateAsync(document, ct);
        await audit.WriteAsync(
            "ApiKeyCreated",
            user.Id,
            null,
            $"{document.Id}:{document.Name}:{document.ExpiresAt:o}",
            correlationId,
            ct);
        return new CreatedApiKeyResponse(
            document.Id,
            document.Name,
            rawKey,
            document.KeyPrefix,
            document.Scopes,
            document.CreatedAt,
            document.ExpiresAt);
    }

    public async Task<IReadOnlyList<ApiKeyResponse>> ListAsync(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        var result = await apiKeys.ListByFilterAsync(
            x => x.UserId == user.Id,
            x => x.CreatedAt,
            orderDescending: true,
            pageSize: 100,
            cancellationToken: ct);
        return result.Select(ToResponse).ToList();
    }

    public async Task RevokeAsync(string apiKeyId, string correlationId, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        var apiKey = await apiKeys.SelectAsync(x => x.Id == apiKeyId && x.UserId == user.Id, ct)
            ?? throw new NotFoundException("API_KEY_NOT_FOUND", "API key was not found.");
        if (apiKey.RevokedAt is null)
        {
            apiKey.RevokedAt = clock.UtcNow;
            await apiKeys.ReplaceByFilterAsync(x => x.Id == apiKey.Id, apiKey, ct);
            await audit.WriteAsync("ApiKeyRevoked", user.Id, apiKey.Id, null, correlationId, ct);
        }
    }

    public async Task<ApiKeyPrincipal?> AuthenticateAsync(string rawKey, CancellationToken ct)
    {
        var keyId = ParseKeyId(rawKey);
        if (keyId is null)
        {
            return null;
        }

        var apiKey = await apiKeys.SelectAsync(x => x.Id == keyId, ct);
        if (apiKey is null
            || apiKey.RevokedAt is not null
            || apiKey.ExpiresAt <= clock.UtcNow
            || !FixedTimeHashEquals(apiKey.KeyHash, Hash(rawKey)))
        {
            return null;
        }

        var user = await users.GetByIdAsync(apiKey.UserId, ct);
        if (user is null || !user.IsActive || user.OrganizationId != apiKey.OrganizationId)
        {
            return null;
        }

        if (apiKey.LastUsedAt is null || apiKey.LastUsedAt < clock.UtcNow.AddMinutes(-5))
        {
            apiKey.LastUsedAt = clock.UtcNow;
            await apiKeys.ReplaceByFilterAsync(x => x.Id == apiKey.Id, apiKey, ct);
        }

        return new ApiKeyPrincipal(
            apiKey.Id,
            user.Id,
            user.Username,
            user.Email,
            user.OrganizationId,
            user.Roles,
            apiKey.Scopes);
    }

    private async Task<UserDocument> GetCurrentUserAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        return await users.GetByIdAsync(userId, ct)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
    }

    private static string? ParseKeyId(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || rawKey.Length > 160)
        {
            return null;
        }

        var parts = rawKey.Split('_', 3);
        return parts.Length == 3 && parts[0] == "zmb" && parts[1].Length == 32
            ? parts[1]
            : null;
    }

    private static bool FixedTimeHashEquals(string storedHash, string suppliedHash)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(storedHash),
                Convert.FromHexString(suppliedHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ApiKeyResponse ToResponse(ApiKeyDocument key) =>
        new(
            key.Id,
            key.Name,
            key.KeyPrefix,
            key.Scopes,
            key.CreatedAt,
            key.ExpiresAt,
            key.LastUsedAt,
            key.RevokedAt);
}
