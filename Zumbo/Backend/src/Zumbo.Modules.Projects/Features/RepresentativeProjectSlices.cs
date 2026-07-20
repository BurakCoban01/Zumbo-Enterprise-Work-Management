using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record CreateProjectRequest(
    string OrganizationId,
    string Key,
    string Name,
    string OwnerUserId,
    string Visibility = ProjectVisibilities.Internal);

public sealed record ProjectResponse(
    string Id,
    string OrganizationId,
    string Key,
    string Name,
    string Visibility,
    IReadOnlyCollection<ProjectMemberResponse> Members,
    IReadOnlyCollection<string> TeamIds,
    bool Archived = false,
    long Version = 0,
    IReadOnlyCollection<ProjectTemplateResponse>? Templates = null,
    IReadOnlyCollection<ProjectComponentResponse>? Components = null,
    IReadOnlyCollection<ProjectVersionResponse>? Versions = null,
    IReadOnlyCollection<ProjectReleaseResponse>? Releases = null,
    IReadOnlyCollection<ProjectMilestoneResponse>? Milestones = null,
    DateTimeOffset? ArchivedAt = null,
    DateTimeOffset? RetainUntil = null) : IVersionedResource;

public sealed record ProjectMemberResponse(string UserId, string Role);
public sealed record ProjectTemplateResponse(
    string Id,
    string Name,
    bool IsDefault,
    bool Archived,
    IReadOnlyCollection<string> DefaultComponentNames);
public sealed record ProjectComponentResponse(string Id, string Name, string? Description, bool Archived);
public sealed record ProjectVersionResponse(string Id, string Name, string Status, DateTimeOffset? ReleasedAt);
public sealed record ProjectReleaseResponse(
    string Id,
    string VersionId,
    string Name,
    string Status,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PublishedAt);
public sealed record ProjectMilestoneResponse(
    string Id,
    string Name,
    DateTimeOffset DueAt,
    string Status,
    DateTimeOffset? CompletedAt);

public sealed class CreateProjectValidator
{
    public static void Validate(CreateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationId)
            || string.IsNullOrWhiteSpace(request.Key)
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            throw new ValidationException("Organization id, project key and name are required.");
        }
    }
}

public sealed class CreateProjectHandler(ProjectService service)
{
    public Task<ProjectResponse> HandleAsync(CreateProjectRequest request, string correlationId, CancellationToken ct) =>
        service.CreateAsync(request, correlationId, ct);
}

public sealed record ListProjectsQuery(string OrganizationId, bool Archived);

public sealed class ListProjectsValidator
{
    public static void Validate(ListProjectsQuery query) => ArgumentNullException.ThrowIfNull(query);
}

public sealed class ListProjectsHandler(ProjectService service)
{
    public Task<IReadOnlyList<ProjectResponse>> HandleAsync(ListProjectsQuery query, CancellationToken ct)
    {
        ListProjectsValidator.Validate(query);
        return service.ListAsync(query.OrganizationId, ct, query.Archived);
    }
}
