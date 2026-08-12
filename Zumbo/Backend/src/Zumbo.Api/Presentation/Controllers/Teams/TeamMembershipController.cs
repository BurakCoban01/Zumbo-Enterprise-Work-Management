using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Teams;

namespace Zumbo.Api.Presentation.Controllers.Teams;

[ApiController]
[Route("/api/teams/{teamId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Teams")]
[ZumboPermission(PermissionCatalog.TeamView)]
[DurableTransaction("Teams")]
public sealed class TeamMembershipController : ApiControllerBase
{
    [HttpPost("members")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.TeamManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> InviteMember(
        [FromRoute] string teamId,
        [FromBody] InviteTeamMemberRequest request,
        [FromServices] TeamService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.InviteAsync(
            teamId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpGet("members")]
    public async Task<IActionResult> ListMembers(
        [FromRoute] string teamId,
        [FromQuery] string? afterMemberId,
        [FromQuery] int? pageSize,
        [FromServices] TeamService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListMembersAsync(
            teamId, afterMemberId, pageSize ?? 50, cancellationToken));

    [HttpPost("invites/accept")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> AcceptInvite(
        [FromRoute] string teamId,
        [FromBody] TeamInviteTokenRequest request,
        [FromServices] TeamService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.AcceptInviteAsync(
            teamId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("invites/decline")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> DeclineInvite(
        [FromRoute] string teamId,
        [FromBody] TeamInviteTokenRequest request,
        [FromServices] TeamService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.DeclineInviteAsync(
            teamId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("invites/{inviteId}/revoke")]
    [ZumboPermission(PermissionCatalog.TeamManage)]
    public async Task<IActionResult> RevokeInvite(
        [FromRoute] string teamId,
        [FromRoute] string inviteId,
        [FromServices] TeamService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RevokeInviteAsync(
            teamId, inviteId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPatch("members/{userId}/role")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.TeamManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> ChangeMemberRole(
        [FromRoute] string teamId,
        [FromRoute] string userId,
        [FromBody] ChangeTeamMemberRoleRequest request,
        [FromServices] TeamService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ChangeMemberRoleAsync(
            teamId, userId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("ownership-transfer")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.TeamManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> TransferOwnership(
        [FromRoute] string teamId,
        [FromBody] TransferTeamOwnershipRequest request,
        [FromServices] TeamService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.TransferOwnershipAsync(
            teamId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpDelete("members/{userIdOrEmail}")]
    [ZumboPermission(PermissionCatalog.TeamManage)]
    public async Task<IActionResult> RemoveMember(
        [FromRoute] string teamId,
        [FromRoute] string userIdOrEmail,
        [FromServices] TeamService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RemoveMemberAsync(
            teamId, userIdOrEmail, HttpContext.TraceIdentifier, cancellationToken));
}
