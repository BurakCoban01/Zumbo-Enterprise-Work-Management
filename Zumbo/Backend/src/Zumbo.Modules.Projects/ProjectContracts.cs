namespace Zumbo.Modules.Projects;

public static class ProjectRoles
{
    public const string Owner = "ProjectOwner";
    public const string Admin = "ProjectAdmin";
    public const string Developer = "Developer";
    public const string Viewer = "Viewer";
}

public static class ProjectVisibilities
{
    public const string Internal = "Internal";
    public const string Private = "Private";

    public static string Normalize(string? visibility)
    {
        if (string.IsNullOrWhiteSpace(visibility)
            || visibility.Equals(Internal, StringComparison.OrdinalIgnoreCase))
        {
            return Internal;
        }

        if (visibility.Equals(Private, StringComparison.OrdinalIgnoreCase))
        {
            return Private;
        }

        throw new Zumbo.SharedKernel.ValidationException("Project visibility must be Internal or Private.");
    }
}

public static class ProjectReleaseStatuses
{
    public const string Draft = "Draft";
    public const string Approved = "Approved";
    public const string Published = "Published";
}

public static class ProjectVersionStatuses
{
    public const string Planned = "Planned";
    public const string Released = "Released";
    public const string Archived = "Archived";
}

public static class ProjectMilestoneStatuses
{
    public const string Open = "Open";
    public const string Completed = "Completed";
}

public static class ProjectCatalogLimits
{
    public const int MaximumDefaultComponentNames = 50;
}

public sealed class ProjectLifecycleOptions
{
    public int ArchiveRetentionDays { get; set; } = 90;
}

public sealed record AddProjectMemberRequest(string UserId, string Role);
public sealed record UpdateProjectRequest(string Name, string Visibility, string? Key = null);
public sealed record ChangeProjectMemberRoleRequest(string Role);
public sealed record TransferProjectOwnershipRequest(string NewOwnerUserId);
public sealed record AddProjectTeamRequest(string TeamId);
public sealed record UpsertProjectTemplateRequest(
    string Name,
    bool IsDefault,
    IReadOnlyCollection<string>? DefaultComponentNames = null);
public sealed record CreateProjectComponentRequest(string Name, string? Description = null);
public sealed record UpdateProjectComponentRequest(string Name, string? Description = null);
public sealed record CreateProjectVersionRequest(string Name);
public sealed record CreateProjectReleaseRequest(
    string VersionId,
    string Name,
    DateTimeOffset? ScheduledAt = null);
public sealed record CreateProjectMilestoneRequest(string Name, DateTimeOffset DueAt);
public sealed record UpdateProjectMilestoneRequest(string Name, DateTimeOffset DueAt);

public sealed record ProjectTeamDirectoryEntry(string Id, string OrganizationId, bool IsActive);

public interface IProjectMemberDirectory
{
    Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct);
}

public interface IProjectOrganizationDirectory
{
    Task EnsureActiveAsync(string organizationId, CancellationToken ct);
}

public interface IProjectTeamDirectory
{
    Task<ProjectTeamDirectoryEntry?> FindAsync(string teamId, CancellationToken ct);
}

public interface IProjectTeamUsageChecker
{
    Task<bool> HasWorkItemsAsync(string projectId, string teamId, CancellationToken ct);
}

public interface IProjectAuditWriter
{
    Task WriteAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}

internal sealed class AllowActiveProjectOrganizationDirectory : IProjectOrganizationDirectory
{
    internal static readonly AllowActiveProjectOrganizationDirectory Instance = new();
    public Task EnsureActiveAsync(string organizationId, CancellationToken ct) => Task.CompletedTask;
}
