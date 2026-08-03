using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed record OrganizationMemberPageResponse(
    IReadOnlyList<OrganizationMemberResponse> Items,
    string? NextCursor,
    int PageSize);
