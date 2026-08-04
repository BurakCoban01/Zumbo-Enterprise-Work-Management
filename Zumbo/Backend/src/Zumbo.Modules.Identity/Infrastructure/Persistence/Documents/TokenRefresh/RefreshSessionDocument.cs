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
