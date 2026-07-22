using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class SecurityHardeningTests
{
    [Fact]
    public void PasswordHasher_ExposesVersionMetadataAndDetectsUpgradeNeed()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var current = hasher.Hash("P@ssword123");
        var legacy = LegacyHash("P@ssword123", 100_000);

        Assert.StartsWith("PBKDF2-SHA256$210000$", current, StringComparison.Ordinal);
        Assert.False(hasher.NeedsRehash(current));
        Assert.True(hasher.Verify("P@ssword123", legacy));
        Assert.True(hasher.NeedsRehash(legacy));
        Assert.True(hasher.NeedsRehash("malformed"));
    }

    [Fact]
    public void JwtIssuer_EmitsActiveKidAndOverlapValidationRejectsRemovedKey()
    {
        var oldKey = "old-signing-key-with-at-least-thirty-two-characters";
        var currentKey = "current-signing-key-with-at-least-thirty-two-chars";
        var overlapping = new JwtOptions
        {
            Issuer = "issuer",
            Audience = "audience",
            ActiveKeyId = "current",
            SigningKeys = new Dictionary<string, string>
            {
                ["old"] = oldKey,
                ["current"] = currentKey
            }
        };
        var issuer = new JwtTokenIssuer();
        var token = issuer.CreateAccessToken(
            new TokenUser("user", "user", "user@local", "org", ["User"], "stamp", "session"),
            overlapping,
            DateTimeOffset.UtcNow);

        Assert.Equal("current", new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid);
        Validate(token, overlapping.ResolveSigningKeys());

        var oldToken = issuer.CreateAccessToken(
            new TokenUser("user", "user", "user@local", "org", ["User"], "stamp", "session"),
            new JwtOptions
            {
                Issuer = "issuer",
                Audience = "audience",
                ActiveKeyId = "old",
                SigningKeys = overlapping.SigningKeys
            },
            DateTimeOffset.UtcNow);
        Validate(oldToken, overlapping.ResolveSigningKeys());
        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            Validate(oldToken, new Dictionary<string, string> { ["current"] = currentKey }));
    }

    [Fact]
    public async Task Login_RehashesLegacyPasswordAndSessionCanBeListedAndRevoked()
    {
        var documents = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(documents);
        var sessionDocuments = new InMemoryDocumentRepository<RefreshSessionDocument>();
        var currentUser = new MutableCurrentUser();
        var audit = new RecordingAuditWriter();
        var user = new UserDocument
        {
            Username = "rehash-user",
            Email = "rehash-user@zumbo.local",
            OrganizationId = "org-rehash",
            PasswordHash = LegacyHash("P@ssword123", 100_000),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        await users.AddAsync(user, CancellationToken.None);
        var service = new IdentityService(
            users,
            new RefreshSessionStore(sessionDocuments),
            new InMemoryDurableTransactionRunner(),
            new Pbkdf2PasswordHasher(),
            new JwtTokenIssuer(),
            Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-with-more-than-32-chars" }),
            Options.Create(new LoginSecurityOptions()),
            Options.Create(new IdentityBootstrapOptions()),
            Options.Create(new PasswordResetOptions()),
            new NullResetNotifier(),
            new PlainMfaProtector(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            new SystemTestClock(),
            currentUser,
            null,
            new FixedSessionClientContext(),
            audit);

        var login = await service.LoginAsync(new LoginRequest(user.Username, "P@ssword123"), CancellationToken.None);
        currentUser.UserId = user.Id;
        currentUser.OrganizationId = user.OrganizationId;
        var upgraded = await users.GetByIdAsync(user.Id, CancellationToken.None);
        var listed = Assert.Single(await service.ListSessionsAsync(CancellationToken.None));

        Assert.False(new Pbkdf2PasswordHasher().NeedsRehash(upgraded!.PasswordHash));
        Assert.Equal("Test laptop", listed.DeviceName);
        Assert.Equal("CLIENT-FINGERPRINT", listed.ClientFingerprint);
        await service.RevokeSessionAsync(listed.Id, "session-revoke-test", CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.RefreshAsync(new RefreshTokenRequest(login.RefreshToken), CancellationToken.None));
        Assert.Contains("SessionRevoked", audit.Actions);
    }

    [Fact]
    public async Task ConcurrentPasswordReset_ConsumesOpaqueTokenExactlyOnce()
    {
        var rawToken = "reset-" + Guid.NewGuid().ToString("N");
        var documents = new InMemoryDocumentRepository<UserDocument>();
        var repository = new UserRepository(documents);
        var user = new UserDocument
        {
            Username = "reset-race",
            Email = "reset-race@zumbo.local",
            OrganizationId = "org-reset-race",
            PasswordHash = new Pbkdf2PasswordHasher().Hash("P@ssword123"),
            PasswordResetTokenHash = RefreshTokenSecurity.Hash(rawToken),
            PasswordResetTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedAt = DateTimeOffset.UtcNow
        };
        await repository.AddAsync(user, CancellationToken.None);
        var coordinatedUsers = new CoordinatedResetUserRepository(repository, user.Id);
        var audit = new RecordingAuditWriter();
        var service = new IdentityService(
            coordinatedUsers,
            new RefreshSessionStore(new InMemoryDocumentRepository<RefreshSessionDocument>()),
            new InMemoryDurableTransactionRunner(),
            new Pbkdf2PasswordHasher(),
            new JwtTokenIssuer(),
            Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-with-more-than-32-chars" }),
            Options.Create(new LoginSecurityOptions()),
            Options.Create(new IdentityBootstrapOptions()),
            Options.Create(new PasswordResetOptions()),
            new NullResetNotifier(),
            new PlainMfaProtector(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            new SystemTestClock(),
            new MutableCurrentUser(),
            null,
            new FixedSessionClientContext(),
            audit);

        var outcomes = await Task.WhenAll(
            CaptureResetAsync(service, rawToken, "N3wP@ssword456"),
            CaptureResetAsync(service, rawToken, "An0therP@ss789"));

        Assert.Single(outcomes, outcome => outcome.Response?.Reset == true);
        Assert.Single(outcomes, outcome => outcome.Error is UnauthorizedException);
        Assert.Null((await repository.GetByIdAsync(user.Id, CancellationToken.None))!.PasswordResetTokenHash);
        Assert.Equal(1, audit.Actions.Count(action => action == "PasswordReset"));
    }

    private static string LegacyHash(string password, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
        return $"PBKDF2-SHA256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static async Task<(PasswordResetResponse? Response, Exception? Error)> CaptureResetAsync(
        IdentityService service,
        string token,
        string password)
    {
        try
        {
            return (await service.ResetPasswordAsync(
                new ResetPasswordRequest(token, password),
                "reset-race",
                CancellationToken.None), null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    private static void Validate(string token, IReadOnlyDictionary<string, string> keys)
    {
        _ = new JwtSecurityTokenHandler().ValidateToken(
            token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "issuer",
                ValidAudience = "audience",
                ClockSkew = TimeSpan.FromMinutes(1),
                IssuerSigningKeyResolver = (_, _, keyId, _) =>
                    keyId is not null && keys.TryGetValue(keyId, out var key)
                        ? [new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)) { KeyId = keyId }]
                        : []
            },
            out _);
    }

    private sealed class SystemTestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class MutableCurrentUser : ICurrentUser
    {
        public string? UserId { get; set; }
        public string? OrganizationId { get; set; }
        public IReadOnlyCollection<string> Roles { get; set; } = ["User"];
    }

    private sealed class FixedSessionClientContext : ISessionClientContext
    {
        public SessionClientInfo GetClientInfo() => new("Test laptop", "CLIENT-FINGERPRINT");
    }

    private sealed class RecordingAuditWriter : IIdentityAuditWriter
    {
        public List<string> Actions { get; } = [];

        public Task WriteAsync(
            string action,
            string entityId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class NullResetNotifier : IPasswordResetNotifier
    {
        public Task SendAsync(string email, string rawToken, DateTimeOffset expiresAt, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class PlainMfaProtector : IMfaSecretProtector
    {
        public string Protect(string secret) => secret;
        public string Unprotect(string protectedSecret) => protectedSecret;
    }

    private sealed class CoordinatedResetUserRepository(IUserRepository inner, string userId) : IUserRepository
    {
        private readonly TaskCompletionSource<bool> readersReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int readers;

        public Task<UserDocument?> GetByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct) =>
            inner.GetByUsernameOrEmailAsync(usernameOrEmail, ct);

        public async Task<UserDocument?> GetByIdAsync(string requestedUserId, CancellationToken ct)
        {
            var result = await inner.GetByIdAsync(requestedUserId, ct);
            if (requestedUserId != userId || Volatile.Read(ref readers) >= 2)
            {
                return result;
            }

            if (Interlocked.Increment(ref readers) == 2)
            {
                readersReady.TrySetResult(true);
            }
            else
            {
                await readersReady.Task.WaitAsync(ct);
            }

            return result;
        }

        public Task<UserDocument?> GetByRefreshTokenAsync(string refreshToken, CancellationToken ct) =>
            inner.GetByRefreshTokenAsync(refreshToken, ct);

        public Task<UserDocument?> GetByPasswordResetTokenAsync(string token, CancellationToken ct) =>
            inner.GetByPasswordResetTokenAsync(token, ct);

        public Task<bool> HasSystemAdminAsync(CancellationToken ct) => inner.HasSystemAdminAsync(ct);

        public Task<IReadOnlyList<UserProfileResponse>> SearchAsync(
            string? search,
            string? organizationId,
            CancellationToken ct) =>
            inner.SearchAsync(search, organizationId, ct);

        public Task AddAsync(UserDocument document, CancellationToken ct) => inner.AddAsync(document, ct);

        public Task UpdateAsync(UserDocument document, CancellationToken ct) => inner.UpdateAsync(document, ct);
    }
}
