using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Identity;

public sealed class RefreshSessionDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string DeviceName { get; set; } = "Unknown client";
    public string ClientFingerprint { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedBySessionId { get; set; }
    public DateTime RetainUntilUtc { get; set; }
    public long Version { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

public interface IRefreshSessionStore
{
    Task<RefreshSessionDocument?> GetByTokenAsync(string rawToken, CancellationToken ct);
    Task<RefreshSessionDocument?> GetByIdAsync(
        string sessionId,
        string userId,
        string organizationId,
        CancellationToken ct);
    Task<IReadOnlyList<RefreshSessionDocument>> ListOwnedAsync(
        string userId,
        string organizationId,
        CancellationToken ct);
    Task CreateAsync(RefreshSessionDocument session, CancellationToken ct);
    Task<bool> RevokeAsync(
        RefreshSessionDocument session,
        DateTimeOffset revokedAt,
        string? replacedBySessionId,
        CancellationToken ct);
    Task<int> RevokeAllAsync(
        string userId,
        string organizationId,
        DateTimeOffset revokedAt,
        CancellationToken ct);
    Task<int> PurgeRetainedAsync(DateTimeOffset now, int batchSize, CancellationToken ct);
}

public sealed class RefreshSessionStore(
    IDocumentRepository<RefreshSessionDocument> sessions) : IRefreshSessionStore
{
    private static readonly TimeSpan ReuseDetectionRetention = TimeSpan.FromDays(30);

    public Task<RefreshSessionDocument?> GetByTokenAsync(string rawToken, CancellationToken ct)
    {
        var tokenHash = RefreshTokenSecurity.Hash(rawToken);
        return sessions.SelectAsync(x => x.TokenHash == tokenHash, ct);
    }

    public Task<RefreshSessionDocument?> GetByIdAsync(
        string sessionId,
        string userId,
        string organizationId,
        CancellationToken ct) =>
        sessions.SelectAsync(
            x => x.Id == sessionId
                && x.UserId == userId
                && x.OrganizationId == organizationId,
            ct);

    public Task<IReadOnlyList<RefreshSessionDocument>> ListOwnedAsync(
        string userId,
        string organizationId,
        CancellationToken ct) =>
        sessions.ListByFilterAsync(
            x => x.UserId == userId && x.OrganizationId == organizationId,
            x => x.LastSeenAt,
            orderDescending: true,
            pageSize: 100,
            cancellationToken: ct);

    public async Task CreateAsync(RefreshSessionDocument session, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId)
            || string.IsNullOrWhiteSpace(session.OrganizationId)
            || string.IsNullOrWhiteSpace(session.TokenHash))
        {
            throw new InvalidOperationException("Refresh session ownership and token hash are required.");
        }

        await sessions.CreateAsync(session, ct);
    }

    public async Task<bool> RevokeAsync(
        RefreshSessionDocument session,
        DateTimeOffset revokedAt,
        string? replacedBySessionId,
        CancellationToken ct)
    {
        if (session.RevokedAt is not null)
        {
            return false;
        }

        session.RevokedAt = revokedAt;
        session.RevokedAtUtc = revokedAt.UtcDateTime;
        session.ReplacedBySessionId = replacedBySessionId;
        var minimumRetention = revokedAt.Add(ReuseDetectionRetention).UtcDateTime;
        if (session.RetainUntilUtc < minimumRetention)
        {
            session.RetainUntilUtc = minimumRetention;
        }

        var result = await sessions.ReplaceByVersionAsync(
            x => x.Id == session.Id
                && x.UserId == session.UserId
                && x.OrganizationId == session.OrganizationId,
            session,
            session.Version,
            ct);
        if (!result.Found)
        {
            return false;
        }

        session.Version = result.Version!.Value;
        return true;
    }

    public async Task<int> RevokeAllAsync(
        string userId,
        string organizationId,
        DateTimeOffset revokedAt,
        CancellationToken ct)
    {
        var revoked = 0;
        string? cursor = null;
        do
        {
            var page = await sessions.ListByCursorAsync(
                x => x.UserId == userId
                    && x.OrganizationId == organizationId
                    && x.RevokedAtUtc == null,
                cursor,
                pageSize: 200,
                cancellationToken: ct);
            foreach (var session in page.Items)
            {
                if (await RevokeAsync(session, revokedAt, null, ct))
                {
                    revoked++;
                }
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return revoked;
    }

    public async Task<int> PurgeRetainedAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken ct)
    {
        var size = Math.Clamp(batchSize, 1, 500);
        var expired = await sessions.ListByFilterAsync(
            x => x.RetainUntilUtc <= now.UtcDateTime,
            x => x.RetainUntilUtc,
            pageSize: size,
            cancellationToken: ct);
        if (expired.Count == 0)
        {
            return 0;
        }

        var ids = expired.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        return checked((int)await sessions.DeleteByFilterAsync(
            x => ids.Contains(x.Id) && x.RetainUntilUtc <= now.UtcDateTime,
            ct));
    }
}
