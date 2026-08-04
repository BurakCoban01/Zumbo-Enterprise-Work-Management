namespace Zumbo.Modules.Organizations;

public sealed record OrganizationMemberPageResponse(
    IReadOnlyList<OrganizationMemberResponse> Items,
    string? NextCursor,
    int PageSize);
