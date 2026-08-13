using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Projects.Application.Features.Portfolio;

namespace Zumbo.Api.Presentation.Controllers.Strategy;

[ApiController]
[Route("/api/portfolios/{portfolioId}/initiatives")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Portfolios")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class PortfolioInitiativesController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromRoute] string portfolioId, [FromBody] SaveInitiativeRequest request, [FromServices] SaveInitiativeHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SaveInitiativeCommand(portfolioId, null, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPut("{initiativeId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string portfolioId, [FromRoute] string initiativeId, [FromBody] SaveInitiativeRequest request, [FromServices] SaveInitiativeHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SaveInitiativeCommand(portfolioId, initiativeId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPost("{initiativeId}/status-updates")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> AddStatusUpdate([FromRoute] string portfolioId, [FromRoute] string initiativeId, [FromBody] AddInitiativeStatusUpdateRequest request, [FromServices] AddInitiativeStatusUpdateHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new AddInitiativeStatusUpdateCommand(portfolioId, initiativeId, request, HttpContext.TraceIdentifier), cancellationToken));
}
