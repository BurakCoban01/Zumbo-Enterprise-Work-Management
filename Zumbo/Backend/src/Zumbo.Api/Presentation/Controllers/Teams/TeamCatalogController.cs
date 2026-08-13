using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Teams;

namespace Zumbo.Api.Presentation.Controllers.Teams;

[ApiController]
[Route("/api/teams")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Teams")]
[ZumboPermission(PermissionCatalog.TeamView)]
[DurableTransaction("Teams")]
public sealed class TeamCatalogController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery, BindRequired] string organizationId,
        [FromQuery] bool? archived,
        [FromServices] ListTeamsHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new ListTeamsQuery(organizationId, archived ?? false),
            cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.TeamManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create(
        [FromBody] CreateTeamRequest request,
        [FromServices] CreateTeamHandler handler,
        CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));
}
