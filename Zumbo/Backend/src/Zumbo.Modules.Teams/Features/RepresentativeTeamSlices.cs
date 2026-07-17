using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed record CreateTeamRequest(string OrganizationId, string Name, string OwnerUserId);

public sealed record TeamResponse(
    string Id,
    string OrganizationId,
    string Name,
    IReadOnlyCollection<TeamMemberResponse> Members,
    bool Archived = false,
    long Version = 0,
    string? InvitationToken = null) : IVersionedResource;

public sealed record TeamMemberResponse(
    string Id,
    string? UserId,
    string Email,
    string Role,
    string Status,
    DateTimeOffset? InvitationExpiresAt,
    DateTimeOffset? RespondedAt);

public sealed record TeamMemberListItemResponse(
    string Id,
    string? UserId,
    string Email,
    string DisplayName,
    string Role,
    string Status,
    DateTimeOffset? InvitationExpiresAt);

public sealed record TeamMemberPageResponse(
    IReadOnlyList<TeamMemberListItemResponse> Items,
    string? NextCursor,
    int PageSize);

public sealed class CreateTeamValidator
{
    public static void Validate(CreateTeamRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationId)
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            throw new ValidationException("Organization id, team name and owner user id are required.");
        }
    }
}

public sealed class CreateTeamHandler(TeamService service)
{
    public Task<TeamResponse> HandleAsync(CreateTeamRequest request, string correlationId, CancellationToken ct) =>
        service.CreateAsync(request, correlationId, ct);
}

public sealed record ListTeamsQuery(string OrganizationId, bool Archived);

public sealed class ListTeamsValidator
{
    public static void Validate(ListTeamsQuery query) => ArgumentNullException.ThrowIfNull(query);
}

public sealed class ListTeamsHandler(TeamService service)
{
    public Task<IReadOnlyList<TeamResponse>> HandleAsync(ListTeamsQuery query, CancellationToken ct)
    {
        ListTeamsValidator.Validate(query);
        return service.ListAsync(query.OrganizationId, ct, query.Archived);
    }
}
