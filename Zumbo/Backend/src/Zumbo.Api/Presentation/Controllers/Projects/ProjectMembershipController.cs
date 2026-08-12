using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;

namespace Zumbo.Api.Presentation.Controllers.Projects;

[ApiController]
[Route("/api/projects/{projectId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Projects")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class ProjectMembershipController : ApiControllerBase
{
    [HttpPost("members")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> AddMember([FromRoute] string projectId, [FromBody] AddProjectMemberRequest request, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.AddMemberAsync(projectId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPatch("members/{userId}/role")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> ChangeMemberRole([FromRoute] string projectId, [FromRoute] string userId, [FromBody] ChangeProjectMemberRoleRequest request, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ChangeMemberRoleAsync(projectId, userId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("ownership-transfer")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> TransferOwnership([FromRoute] string projectId, [FromBody] TransferProjectOwnershipRequest request, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.TransferOwnershipAsync(projectId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpDelete("members/{userId}")]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    public async Task<IActionResult> RemoveMember([FromRoute] string projectId, [FromRoute] string userId, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RemoveMemberAsync(projectId, userId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("teams")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> AddTeam([FromRoute] string projectId, [FromBody] AddProjectTeamRequest request, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.AddTeamAsync(projectId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpDelete("teams/{teamId}")]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    public async Task<IActionResult> RemoveTeam([FromRoute] string projectId, [FromRoute] string teamId, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RemoveTeamAsync(projectId, teamId, HttpContext.TraceIdentifier, cancellationToken));
}
