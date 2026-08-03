using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

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
