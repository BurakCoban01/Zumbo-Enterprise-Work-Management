using Zumbo.Modules.Projects;
using Zumbo.BuildingBlocks.Application.Security;

using static ApiEndpointResults;

internal static class ProjectsEndpoints
{
    internal static IServiceCollection AddProjectsModule(this IServiceCollection services)
    {
        services.AddOptions<ProjectLifecycleOptions>()
            .BindConfiguration("ProjectLifecycle")
            .Validate(
                options => options.ArchiveRetentionDays is >= 30 and <= 3650,
                "ProjectLifecycle:ArchiveRetentionDays must be between 30 and 3650 days.")
            .ValidateOnStart();
        services.AddScoped<IProjectResourcePolicy, ProjectResourcePolicyAdapter>();
        services.AddScoped<IProjectMemberDirectory, ProjectMemberDirectoryAdapter>();
        services.AddScoped<IProjectOrganizationDirectory, ProjectOrganizationDirectoryAdapter>();
        services.AddScoped<IProjectTeamDirectory, ProjectTeamDirectoryAdapter>();
        services.AddScoped<IProjectTeamUsageChecker, ProjectTeamUsageCheckerAdapter>();
        services.AddScoped<IProjectAuditWriter, ProjectAuditWriterAdapter>();
        services.AddScoped<ProjectService>();
        services.AddScoped<CreateProjectHandler>();
        services.AddScoped<ListProjectsHandler>();
        return services;
    }

    internal static void MapProjectsEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/projects").WithTags("Projects").RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProjectView);

        group.MapGet("/", async (string organizationId, bool? archived, ListProjectsHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new ListProjectsQuery(organizationId, archived ?? false), ct), http));

        group.MapGet("/{projectId}", async (string projectId, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.GetAsync(projectId, ct), http));

        group.MapPost("/", async (CreateProjectRequest request, CreateProjectHandler handler, HttpContext http, CancellationToken ct) =>
            Created(await handler.HandleAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPost("/{projectId}/members", async (string projectId, AddProjectMemberRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AddMemberAsync(projectId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPut("/{projectId}", async (string projectId, UpdateProjectRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpdateAsync(projectId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPost("/{projectId}/teams", async (string projectId, AddProjectTeamRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AddTeamAsync(projectId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapDelete("/{projectId}/teams/{teamId}", async (string projectId, string teamId, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RemoveTeamAsync(projectId, teamId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPatch("/{projectId}/members/{userId}/role", async (
            string projectId,
            string userId,
            ChangeProjectMemberRoleRequest request,
            ProjectService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ChangeMemberRoleAsync(projectId, userId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPost("/{projectId}/ownership-transfer", async (
            string projectId,
            TransferProjectOwnershipRequest request,
            ProjectService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.TransferOwnershipAsync(projectId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapDelete("/{projectId}/members/{userId}", async (string projectId, string userId, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RemoveMemberAsync(projectId, userId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapDelete("/{projectId}", async (string projectId, ProjectService service, HttpContext http, CancellationToken ct) =>
        {
            await service.ArchiveAsync(projectId, CorrelationId(http), ct);
            return Ok(new { archived = true }, http);
        }).WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPost("/{projectId}/restore", async (string projectId, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RestoreAsync(projectId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPost("/{projectId}/templates", async (
            string projectId, UpsertProjectTemplateRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpsertTemplateAsync(projectId, null, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPut("/{projectId}/templates/{templateId}", async (
            string projectId, string templateId, UpsertProjectTemplateRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpsertTemplateAsync(projectId, templateId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapDelete("/{projectId}/templates/{templateId}", async (
            string projectId, string templateId, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ArchiveTemplateAsync(projectId, templateId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPost("/{projectId}/components", async (
            string projectId, CreateProjectComponentRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.CreateComponentAsync(projectId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPut("/{projectId}/components/{componentId}", async (
            string projectId, string componentId, UpdateProjectComponentRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpdateComponentAsync(projectId, componentId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapDelete("/{projectId}/components/{componentId}", async (
            string projectId, string componentId, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ArchiveComponentAsync(projectId, componentId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPost("/{projectId}/versions", async (
            string projectId, CreateProjectVersionRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.CreateVersionAsync(projectId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapDelete("/{projectId}/versions/{versionId}", async (
            string projectId, string versionId, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ArchiveVersionAsync(projectId, versionId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPost("/{projectId}/releases", async (
            string projectId, CreateProjectReleaseRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.CreateReleaseAsync(projectId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPost("/{projectId}/releases/{releaseId}/approve", async (
            string projectId, string releaseId, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ApproveReleaseAsync(projectId, releaseId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ReleaseApprove);

        group.MapPost("/{projectId}/releases/{releaseId}/publish", async (
            string projectId, string releaseId, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.PublishReleaseAsync(projectId, releaseId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ReleasePublish);

        group.MapPost("/{projectId}/milestones", async (
            string projectId, CreateProjectMilestoneRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.CreateMilestoneAsync(projectId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPut("/{projectId}/milestones/{milestoneId}", async (
            string projectId, string milestoneId, UpdateProjectMilestoneRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpdateMilestoneAsync(projectId, milestoneId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);

        group.MapPost("/{projectId}/milestones/{milestoneId}/complete", async (
            string projectId, string milestoneId, ProjectService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.CompleteMilestoneAsync(projectId, milestoneId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.ProjectManage);
    }
}
