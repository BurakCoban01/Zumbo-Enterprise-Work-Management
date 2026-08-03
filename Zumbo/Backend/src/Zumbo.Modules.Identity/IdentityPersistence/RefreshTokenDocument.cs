using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class RefreshTokenDocument
{
    public string TokenHash { get; set; } = string.Empty;
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
