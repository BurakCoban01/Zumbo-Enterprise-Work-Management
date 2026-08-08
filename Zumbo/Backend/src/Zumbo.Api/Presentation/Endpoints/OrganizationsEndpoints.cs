using Zumbo.Api.Composition.Modules.Organizations;
using Zumbo.Modules.Organizations;
using Zumbo.BuildingBlocks.Application.Security;

using static ApiEndpointResults;

internal static class OrganizationsEndpoints
{
    internal static IServiceCollection AddOrganizationsModule(this IServiceCollection services) =>
        services.AddOrganizationServices();

    internal static void MapOrganizationsEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations").WithTags("Organizations").RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.OrganizationView);

        group.MapGet("/", async (ListOrganizationsHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new ListOrganizationsQuery(), ct), http));

        group.MapPost("/", async (CreateOrganizationRequest request, CreateOrganizationHandler handler, HttpContext http, CancellationToken ct) =>
            Created(await handler.HandleAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.OrganizationManage);

        group.MapPut("/{organizationId}", async (string organizationId, UpdateOrganizationRequest request, OrganizationService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpdateAsync(organizationId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.OrganizationManage);

        group.MapPost("/{organizationId}/ownership-transfer", async (
            string organizationId,
            TransferOrganizationOwnershipRequest request,
            OrganizationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.TransferOwnershipAsync(organizationId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.OrganizationManage);

        group.MapPost("/{organizationId}/suspend", async (
            string organizationId,
            SuspendOrganizationRequest request,
            OrganizationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SuspendAsync(organizationId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.OrganizationManage);

        group.MapPost("/{organizationId}/archive", async (
            string organizationId,
            OrganizationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ArchiveAsync(organizationId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.OrganizationManage);

        group.MapPost("/{organizationId}/restore", async (
            string organizationId,
            OrganizationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.RestoreAsync(organizationId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.OrganizationManage);

        group.MapGet("/{organizationId}/members", async (
            string organizationId,
            string? afterUserId,
            int? pageSize,
            OrganizationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListMembersAsync(organizationId, afterUserId, pageSize ?? 50, ct), http));

        group.MapPost("/{organizationId}/departments", async (string organizationId, CreateDepartmentRequest request, OrganizationService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.CreateDepartmentAsync(organizationId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.OrganizationManage);

        group.MapPut("/{organizationId}/departments/{departmentId}", async (string organizationId, string departmentId, UpdateDepartmentRequest request, OrganizationService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpdateDepartmentAsync(organizationId, departmentId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.OrganizationManage);

        group.MapDelete("/{organizationId}/departments/{departmentId}", async (string organizationId, string departmentId, OrganizationService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.DeleteDepartmentAsync(organizationId, departmentId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.OrganizationManage);

        group.MapPost("/{organizationId}/departments/{departmentId}/members", async (string organizationId, string departmentId, AssignDepartmentMemberRequest request, OrganizationService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AssignMemberAsync(organizationId, departmentId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.OrganizationManage);

        group.MapPatch("/{organizationId}/departments/{departmentId}/members/{userId}", async (
            string organizationId,
            string departmentId,
            string userId,
            UpdateDepartmentMemberRequest request,
            OrganizationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.UpdateMemberPositionAsync(organizationId, departmentId, userId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.OrganizationManage);

        group.MapDelete("/{organizationId}/departments/{departmentId}/members/{userId}", async (
            string organizationId,
            string departmentId,
            string userId,
            OrganizationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.RemoveMemberAsync(organizationId, departmentId, userId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.OrganizationManage);
    }
}
