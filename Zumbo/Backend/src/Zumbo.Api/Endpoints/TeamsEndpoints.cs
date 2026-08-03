using Zumbo.Modules.Teams;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class TeamsEndpoints
{
    internal static IServiceCollection AddTeamsModule(this IServiceCollection services)
    {
        services.AddScoped<ITeamUserDirectory, TeamUserDirectoryAdapter>();
        services.AddScoped<ITeamOrganizationDirectory, TeamOrganizationDirectoryAdapter>();
        services.AddScoped<ITeamAuditWriter, TeamAuditWriterAdapter>();
        services.AddScoped<DurableTeamInvitationPublisher>();
        services.AddScoped<ITeamInvitationNotifier>(provider =>
            provider.GetRequiredService<DurableTeamInvitationPublisher>());
        services.AddScoped<IDurableEventHandler, TeamInvitationNotificationHandler>();
        services.AddScoped<TeamTransactionFilter>();
        services.AddScoped<TeamService>();
        services.AddScoped<CreateTeamHandler>(provider => new CreateTeamHandler(
            provider.GetRequiredService<IDocumentRepository<TeamDocument>>(),
            provider.GetRequiredService<ITeamUserDirectory>(),
            provider.GetRequiredService<ITeamOrganizationDirectory>(),
            provider.GetRequiredService<ITeamAuditWriter>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<ListTeamsHandler>(provider => new ListTeamsHandler(
            provider.GetRequiredService<IDocumentRepository<TeamDocument>>(),
            provider.GetRequiredService<ITeamOrganizationDirectory>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>()));
        return services;
    }

    internal static void MapTeamsEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/teams").WithTags("Teams").RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.TeamView);
        group.AddEndpointFilter<TeamTransactionFilter>();

        group.MapGet("/", async (string organizationId, bool? archived, ListTeamsHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new ListTeamsQuery(organizationId, archived ?? false), ct), http));

        group.MapPost("/", async (CreateTeamRequest request, CreateTeamHandler handler, HttpContext http, CancellationToken ct) =>
            Created(await handler.HandleAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.TeamManage);

        group.MapPost("/{teamId}/members", async (string teamId, InviteTeamMemberRequest request, TeamService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.InviteAsync(teamId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.TeamManage);

        group.MapGet("/{teamId}/members", async (
            string teamId,
            string? afterMemberId,
            int? pageSize,
            TeamService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListMembersAsync(teamId, afterMemberId, pageSize ?? 50, ct), http));

        group.MapPut("/{teamId}", async (string teamId, UpdateTeamRequest request, TeamService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.UpdateAsync(teamId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.TeamManage);

        group.MapPost("/{teamId}/invites/accept", async (
            string teamId,
            TeamInviteTokenRequest request,
            TeamService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.AcceptInviteAsync(teamId, request, CorrelationId(http), ct), http));

        group.MapPost("/{teamId}/invites/decline", async (
            string teamId,
            TeamInviteTokenRequest request,
            TeamService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.DeclineInviteAsync(teamId, request, CorrelationId(http), ct), http));

        group.MapPost("/{teamId}/invites/{inviteId}/revoke", async (
            string teamId,
            string inviteId,
            TeamService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.RevokeInviteAsync(teamId, inviteId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.TeamManage);

        group.MapPatch("/{teamId}/members/{userId}/role", async (
            string teamId,
            string userId,
            ChangeTeamMemberRoleRequest request,
            TeamService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ChangeMemberRoleAsync(teamId, userId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.TeamManage);

        group.MapPost("/{teamId}/ownership-transfer", async (
            string teamId,
            TransferTeamOwnershipRequest request,
            TeamService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.TransferOwnershipAsync(teamId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.TeamManage);

        group.MapDelete("/{teamId}/members/{userIdOrEmail}", async (string teamId, string userIdOrEmail, TeamService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RemoveMemberAsync(teamId, userIdOrEmail, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.TeamManage);

        group.MapDelete("/{teamId}", async (string teamId, TeamService service, HttpContext http, CancellationToken ct) =>
        {
            await service.ArchiveAsync(teamId, CorrelationId(http), ct);
            return Ok(new { archived = true }, http);
        }).WithZumboPermission(PermissionCatalog.TeamManage);

        group.MapPost("/{teamId}/restore", async (string teamId, TeamService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RestoreAsync(teamId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.TeamManage);
    }
}
