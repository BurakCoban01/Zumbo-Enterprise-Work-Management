using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Teams;

public static class TeamMemberStatuses
{
    public const string Active = "Active";
    public const string Invited = "Invited";
    public const string Declined = "Declined";
    public const string Revoked = "Revoked";
    public const string Expired = "Expired";
}

public static class TeamRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Member = "Member";
}

public sealed record UpdateTeamRequest(string Name);
public sealed record InviteTeamMemberRequest(string Email, string Role);
public sealed record TeamInviteTokenRequest(string Token);
public sealed record ChangeTeamMemberRoleRequest(string Role);
public sealed record TransferTeamOwnershipRequest(string NewOwnerUserId);
public sealed record TeamUserDirectoryEntry(
    string Id,
    string Email,
    string OrganizationId,
    bool IsActive,
    string? DisplayName = null);

public interface ITeamUserDirectory
{
    Task<TeamUserDirectoryEntry?> FindByIdAsync(string userId, CancellationToken ct);
    Task<TeamUserDirectoryEntry?> FindByEmailAsync(string email, CancellationToken ct);
}

public interface ITeamOrganizationDirectory
{
    Task EnsureActiveAsync(string organizationId, CancellationToken ct);
}

public interface ITeamAuditWriter
{
    Task WriteAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}

public interface ITeamInvitationNotifier
{
    Task NotifyAsync(
        string organizationId,
        string userId,
        string teamId,
        string inviteId,
        string teamName,
        string invitedByUserId,
        string correlationId,
        CancellationToken ct);
}

internal sealed class AllowActiveTeamOrganizationDirectory : ITeamOrganizationDirectory
{
    internal static readonly AllowActiveTeamOrganizationDirectory Instance = new();
    public Task EnsureActiveAsync(string organizationId, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class NoOpTeamInvitationNotifier : ITeamInvitationNotifier
{
    internal static readonly NoOpTeamInvitationNotifier Instance = new();

    public Task NotifyAsync(
        string organizationId,
        string userId,
        string teamId,
        string inviteId,
        string teamName,
        string invitedByUserId,
        string correlationId,
        CancellationToken ct) => Task.CompletedTask;
}

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
