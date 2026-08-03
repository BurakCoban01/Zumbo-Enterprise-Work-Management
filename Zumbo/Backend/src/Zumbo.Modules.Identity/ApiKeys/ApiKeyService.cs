using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class ApiKeyService(
    IApiKeyStore apiKeys,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IMfaSecretProtector mfaSecretProtector,
    IIdentityAuditWriter audit,
    IClock clock,
    ICurrentUser currentUser)
{
    private static readonly string[] DefaultScopes = [ApiKeyScopes.Full];

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

        var scopes = (request.Scopes ?? DefaultScopes)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (scopes.Count is 0 or > 64 || scopes.Any(scope => !ApiKeyScopes.IsValid(scope)))
        {
            throw new ValidationException("API key scopes are invalid.");
        }

        var now = clock.UtcNow;
        _ = await apiKeys.PurgeExpiredAsync(now, 100, ct);
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
            ExpiresAt = expiresAt,
            ExpiresAtUtc = expiresAt.UtcDateTime
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
        var result = await apiKeys.ListOwnedAsync(user.Id, user.OrganizationId, ct);
        return result.Select(ToResponse).ToList();
    }

    public async Task RevokeAsync(string apiKeyId, string correlationId, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        DocumentConcurrencyException? lastConflict = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var apiKey = await apiKeys.GetOwnedAsync(apiKeyId, user.Id, user.OrganizationId, ct)
                ?? throw new NotFoundException("API_KEY_NOT_FOUND", "API key was not found.");
            if (apiKey.RevokedAt is not null)
            {
                return;
            }

            apiKey.RevokedAt = clock.UtcNow;
            apiKey.RevokedAtUtc = apiKey.RevokedAt.Value.UtcDateTime;
            try
            {
                if (!await apiKeys.ReplaceOwnedAsync(apiKey, ct))
                {
                    throw new NotFoundException("API_KEY_NOT_FOUND", "API key was not found.");
                }

                await audit.WriteAsync("ApiKeyRevoked", user.Id, apiKey.Id, null, correlationId, ct);
                return;
            }
            catch (DocumentConcurrencyException conflict)
            {
                lastConflict = conflict;
            }
        }

        throw lastConflict!;
    }

    public async Task<ApiKeyPrincipal?> AuthenticateAsync(string rawKey, CancellationToken ct)
    {
        var keyId = ParseKeyId(rawKey);
        if (keyId is null)
        {
            return null;
        }

        var apiKey = await apiKeys.GetByIdAsync(keyId, ct);
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
            try
            {
                if (!await apiKeys.ReplaceOwnedAsync(apiKey, ct))
                {
                    return null;
                }
            }
            catch (DocumentConcurrencyException)
            {
                var current = await apiKeys.GetOwnedAsync(
                    apiKey.Id,
                    apiKey.UserId,
                    apiKey.OrganizationId,
                    ct);
                if (current is null
                    || current.RevokedAt is not null
                    || current.ExpiresAt <= clock.UtcNow
                    || !FixedTimeHashEquals(current.KeyHash, Hash(rawKey)))
                {
                    return null;
                }

                apiKey = current;
            }
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
