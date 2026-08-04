using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class UserDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? PasswordResetTokenHash { get; set; }
    public DateTimeOffset? PasswordResetTokenExpiresAt { get; set; }
    public bool MfaEnabled { get; set; }
    public string? MfaSecretProtected { get; set; }
    public string? PendingMfaSecretProtected { get; set; }
    public DateTimeOffset? PendingMfaExpiresAt { get; set; }
    public List<string> MfaRecoveryCodeHashes { get; set; } = [];
    public List<string> Roles { get; set; } = ["User"];
    public List<RefreshTokenDocument> RefreshTokens { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
}
