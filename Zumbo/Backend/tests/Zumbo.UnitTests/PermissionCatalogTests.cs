using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.UnitTests;

public sealed class PermissionCatalogTests
{
    [Theory]
    [InlineData("ProjectOwner", PermissionCatalog.BoardManage, true)]
    [InlineData("ProjectAdmin", PermissionCatalog.WorkItemApprove, true)]
    [InlineData("Developer", PermissionCatalog.WorkItemUpdate, true)]
    [InlineData("Developer", PermissionCatalog.WorkItemDelete, false)]
    [InlineData("Viewer", PermissionCatalog.BoardView, true)]
    [InlineData("Viewer", PermissionCatalog.WorkflowView, true)]
    [InlineData("Viewer", PermissionCatalog.WorkflowManage, false)]
    [InlineData("Viewer", PermissionCatalog.CommentCreate, true)]
    [InlineData("Viewer", PermissionCatalog.WorkItemUpdate, false)]
    [InlineData("Unknown", PermissionCatalog.WorkItemView, false)]
    public void ProjectRoleMatrix_IsCentralAndDeterministic(
        string role,
        string permission,
        bool expected)
    {
        Assert.Equal(expected, PermissionCatalog.HasProjectPermission(role, permission));
    }

    [Fact]
    public void SystemRoleMatrix_UsesOnlySystemAdminWildcard()
    {
        Assert.True(PermissionCatalog.HasSystemPermission(["SystemAdmin"], "Any.Future.Permission"));
        Assert.True(PermissionCatalog.HasSystemPermission(["OrganizationAdmin"], PermissionCatalog.UserRoleManage));
        Assert.False(PermissionCatalog.HasSystemPermission(["OrganizationAdmin"], PermissionCatalog.AuditReadAll));
        Assert.False(PermissionCatalog.HasSystemPermission(["User"], PermissionCatalog.WorkItemView));
    }

    [Fact]
    public void AssignablePermissionCatalog_IsClosed()
    {
        Assert.True(PermissionCatalog.IsKnownAssignablePermission(PermissionCatalog.ReleaseApprove));
        Assert.True(PermissionCatalog.IsKnownAssignablePermission(PermissionCatalog.WorkItemView));
        Assert.False(PermissionCatalog.IsKnownAssignablePermission("Uncatalogued.Permission"));
        Assert.DoesNotContain(PermissionCatalog.All, PermissionCatalog.AssignablePermissions);
    }
}
