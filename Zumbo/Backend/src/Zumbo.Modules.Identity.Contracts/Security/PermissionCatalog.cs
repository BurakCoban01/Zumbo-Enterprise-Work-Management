namespace Zumbo.BuildingBlocks.Application.Security;

public static class PermissionCatalog
{
    public const string All = "*";
    public const string ProfileRead = "ProfileRead";
    public const string OrganizationManage = "OrganizationManage";
    public const string OrganizationView = "OrganizationView";
    public const string UserRoleManage = "UserRoleManage";
    public const string AuditReadAll = "AuditReadAll";
    public const string AuditRead = "AuditRead";
    public const string TeamView = "TeamView";
    public const string TeamManage = "TeamManage";
    public const string ProjectView = "ProjectView";
    public const string ProjectManage = "ProjectManage";
    public const string NotificationView = "NotificationView";
    public const string NotificationManage = "NotificationManage";
    public const string OperationsManage = "OperationsManage";
    public const string IntegrationManage = "IntegrationManage";
    public const string BoardView = "BoardView";
    public const string BoardManage = "BoardManage";
    public const string WorkflowView = "WorkflowView";
    public const string WorkflowManage = "WorkflowManage";
    public const string WorkItemView = "WorkItemView";
    public const string WorkItemCreate = "WorkItemCreate";
    public const string WorkItemUpdate = "WorkItemUpdate";
    public const string WorkItemAssign = "WorkItemAssign";
    public const string WorkItemMove = "WorkItemMove";
    public const string WorkItemDelete = "WorkItemDelete";
    public const string WorkItemLink = "WorkItemLink";
    public const string WorkItemApprove = "WorkItemApprove";
    public const string CommentCreate = "CommentCreate";
    public const string AttachmentCreate = "AttachmentCreate";
    public const string AttachmentDelete = "AttachmentDelete";
    public const string WorkLogCreate = "WorkLogCreate";
    public const string ReleaseApprove = "Release.Approve";
    public const string ReleasePublish = "Release.Publish";

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> SystemRoles { get; } =
        CreateRoleMap(
            ("User", [ProfileRead]),
            ("OrganizationAdmin", [OrganizationManage, UserRoleManage, IntegrationManage]),
            ("AuditReader", [AuditReadAll]),
            ("SystemAdmin", [All]));

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> ProjectRoles { get; } =
        CreateRoleMap(
            ("ProjectOwner",
            [
                BoardView, BoardManage, WorkflowView, WorkflowManage, WorkItemView, WorkItemCreate, WorkItemUpdate, WorkItemAssign,
                WorkItemMove, WorkItemDelete, WorkItemLink, WorkItemApprove, CommentCreate,
                AttachmentCreate, AttachmentDelete, WorkLogCreate
            ]),
            ("ProjectAdmin",
            [
                BoardView, BoardManage, WorkflowView, WorkflowManage, WorkItemView, WorkItemCreate, WorkItemUpdate, WorkItemAssign,
                WorkItemMove, WorkItemDelete, WorkItemLink, WorkItemApprove, CommentCreate,
                AttachmentCreate, AttachmentDelete, WorkLogCreate
            ]),
            ("Developer",
            [
                BoardView, WorkflowView, WorkItemView, WorkItemCreate, WorkItemUpdate, WorkItemAssign, WorkItemMove,
                WorkItemLink, CommentCreate, AttachmentCreate, AttachmentDelete, WorkLogCreate
            ]),
            ("Viewer", [BoardView, WorkflowView, WorkItemView, CommentCreate]));

    public static IReadOnlySet<string> AssignablePermissions { get; } =
        SystemRoles.Values
            .SelectMany(x => x)
            .Concat(ProjectRoles.Values.SelectMany(x => x))
            .Append(ReleaseApprove)
            .Append(ReleasePublish)
            .Where(x => x != All)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> EndpointPermissions { get; } =
        AssignablePermissions
            .Concat(
            [
                OrganizationView, AuditRead, TeamView, TeamManage, ProjectView, ProjectManage,
                NotificationView, NotificationManage, OperationsManage, IntegrationManage
            ])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool HasSystemPermission(IEnumerable<string> roles, string permission) =>
        roles.Any(role => RoleHasPermission(SystemRoles, role, permission));

    public static bool HasProjectPermission(string role, string permission) =>
        RoleHasPermission(ProjectRoles, role, permission);

    public static bool IsSystemAdministrator(IEnumerable<string> roles) =>
        roles.Contains("SystemAdmin", StringComparer.OrdinalIgnoreCase);

    public static bool IsKnownAssignablePermission(string permission) =>
        AssignablePermissions.Contains(permission);

    public static bool IsKnownEndpointPermission(string permission) =>
        EndpointPermissions.Contains(permission);

    private static bool RoleHasPermission(
        IReadOnlyDictionary<string, IReadOnlySet<string>> catalog,
        string role,
        string permission) =>
        catalog.TryGetValue(role, out var permissions)
        && (permissions.Contains(All) || permissions.Contains(permission));

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> CreateRoleMap(
        params (string Role, string[] Permissions)[] definitions) =>
        definitions.ToDictionary(
            x => x.Role,
            x => (IReadOnlySet<string>)x.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
}
