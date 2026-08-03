namespace Zumbo.BuildingBlocks.Application.Security;

public interface IProjectResourcePolicy
{
    Task<ProjectResourceAuthorization> AuthorizeAsync(
        string projectId,
        string permission,
        CancellationToken cancellationToken);
}
