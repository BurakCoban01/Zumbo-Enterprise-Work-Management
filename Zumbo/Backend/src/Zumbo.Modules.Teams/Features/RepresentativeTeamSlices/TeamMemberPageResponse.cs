using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed record TeamMemberPageResponse(
    IReadOnlyList<TeamMemberListItemResponse> Items,
    string? NextCursor,
    int PageSize);
