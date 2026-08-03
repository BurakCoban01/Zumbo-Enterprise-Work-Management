namespace Zumbo.BuildingBlocks.Application.Security;

public sealed record ProjectResourceAuthorization(
    string ProjectId,
    string OrganizationId,
    string UserId,
    string? ProjectRole,
    bool IsSystemAdministrator);
