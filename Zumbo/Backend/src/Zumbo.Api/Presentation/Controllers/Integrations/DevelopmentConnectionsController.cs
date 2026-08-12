using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Development.Connections;

namespace Zumbo.Api.Presentation.Controllers.Integrations;

[ApiController]
[Route("/api/integrations/development")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Development integrations")]
[ZumboPermission(PermissionCatalog.IntegrationManage)]
[DurableTransaction("WorkItems")]
public sealed class DevelopmentConnectionsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromServices] ListConnectionsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ListConnectionsQuery(), cancellationToken));

    [HttpGet("{connectionId}")]
    public async Task<IActionResult> Get([FromRoute] string connectionId, [FromServices] GetConnectionHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetConnectionQuery(connectionId), cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromBody] CreateDevelopmentConnectionRequest request, [FromServices] CreateConnectionHandler handler, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(new CreateConnectionCommand(request, HttpContext.TraceIdentifier), cancellationToken));
}
