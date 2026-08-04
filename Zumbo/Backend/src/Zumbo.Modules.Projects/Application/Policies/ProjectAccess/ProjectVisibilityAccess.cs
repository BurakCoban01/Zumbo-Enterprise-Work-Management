using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.Modules.Projects;

public static class ProjectVisibilityAccess
{
    public static bool CanView(string? visibility, IEnumerable<string> memberUserIds, string userId) =>
        ProjectVisibilities.Normalize(visibility) == ProjectVisibilities.Internal
        || memberUserIds.Contains(userId, StringComparer.Ordinal);

    public static bool IsReadPermission(string permission) =>
        permission is PermissionCatalog.ProjectView
            or PermissionCatalog.BoardView
            or PermissionCatalog.WorkflowView
            or PermissionCatalog.WorkItemView;
}
