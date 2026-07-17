namespace Zumbo.BuildingBlocks.Application.Security;

public sealed record ProjectResourceAuthorization(
    string ProjectId,
    string OrganizationId,
    string UserId,
    string? ProjectRole,
    bool IsSystemAdministrator);

public interface IProjectResourcePolicy
{
    Task<ProjectResourceAuthorization> AuthorizeAsync(
        string projectId,
        string permission,
        CancellationToken cancellationToken);
}
