namespace Zumbo.BuildingBlocks.Application.Security;

public sealed record TokenUser(
    string Id,
    string Username,
    string Email,
    string OrganizationId,
    IReadOnlyCollection<string> Roles,
    string SecurityStamp,
    string SessionId);
