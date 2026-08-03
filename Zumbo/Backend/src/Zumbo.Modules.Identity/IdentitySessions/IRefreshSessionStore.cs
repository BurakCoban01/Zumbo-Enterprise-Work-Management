using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Identity;

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
