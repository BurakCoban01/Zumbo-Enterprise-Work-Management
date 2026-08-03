using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed record TeamMemberResponse(
    string Id,
    string? UserId,
    string Email,
    string Role,
    string Status,
    DateTimeOffset? InvitationExpiresAt,
    DateTimeOffset? RespondedAt);
