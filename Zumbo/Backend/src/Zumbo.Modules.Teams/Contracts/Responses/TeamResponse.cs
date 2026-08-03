using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Teams;

public sealed record TeamResponse(
    string Id,
    string OrganizationId,
    string Name,
    IReadOnlyCollection<TeamMemberResponse> Members,
    bool Archived = false,
    long Version = 0,
    string? InvitationToken = null) : IVersionedResource;
