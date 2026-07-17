using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;

public static class OrganizationStatuses
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Archived = "Archived";
}

public sealed class OrganizationLifecycleOptions
{
    public int ArchiveRetentionDays { get; set; } = 90;
}

public sealed record UpdateOrganizationRequest(string Name, string? TenantKey = null);
public sealed record TransferOrganizationOwnershipRequest(string NewOwnerUserId);
public sealed record SuspendOrganizationRequest(string? Reason = null);
public sealed record CreateDepartmentRequest(string Name, string? ParentDepartmentId);
public sealed record UpdateDepartmentRequest(string Name, string? ParentDepartmentId);
public sealed record AssignDepartmentMemberRequest(string UserId, string Position);
public sealed record UpdateDepartmentMemberRequest(string Position);

public sealed class OrganizationDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string TenantKey { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string Status { get; set; } = OrganizationStatuses.Active;
    public string? SuspensionReason { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset? RetainUntil { get; set; }
    public long Version { get; set; }
    public List<DepartmentDocument> Departments { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DepartmentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? ParentDepartmentId { get; set; }
    public List<DepartmentMemberDocument> Members { get; set; } = [];
}

public sealed class DepartmentMemberDocument
{
    public string UserId { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
}

public interface IOrganizationMemberDirectory
{
    Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct);
}

public interface IOrganizationAuditWriter
{
    Task WriteAsync(
        string action,
        string organizationId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}
