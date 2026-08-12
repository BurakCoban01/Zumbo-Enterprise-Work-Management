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
public sealed class TeamLifecycleController : ApiControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.TeamManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update(
        [FromRoute] string teamId,
        [FromBody] UpdateTeamRequest request,
        [FromServices] TeamService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.UpdateAsync(
            teamId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpDelete]
    [ZumboPermission(PermissionCatalog.TeamManage)]
    public async Task<IActionResult> Archive(
        [FromRoute] string teamId,
        [FromServices] TeamService service,
        CancellationToken cancellationToken)
    {
        await service.ArchiveAsync(teamId, HttpContext.TraceIdentifier, cancellationToken);
        return OkEnvelopeResult(new { archived = true });
    }

    [HttpPost("restore")]
    [ZumboPermission(PermissionCatalog.TeamManage)]
    public async Task<IActionResult> Restore(
        [FromRoute] string teamId,
        [FromServices] TeamService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RestoreAsync(
            teamId, HttpContext.TraceIdentifier, cancellationToken));
}
