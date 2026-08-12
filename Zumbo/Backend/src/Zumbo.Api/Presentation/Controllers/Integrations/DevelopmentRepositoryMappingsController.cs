using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;

namespace Zumbo.Api.Presentation.Controllers.Integrations;

[ApiController]
[Route("/api/integrations/development")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Development integrations")]
[ZumboPermission(PermissionCatalog.IntegrationManage)]
[DurableTransaction("WorkItems")]
public sealed class DevelopmentRepositoryMappingsController : ApiControllerBase
{
    [HttpGet("{connectionId}/mappings")]
    public async Task<IActionResult> List([FromRoute] string connectionId, [FromServices] ListConnectionMappingsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ListConnectionMappingsQuery(connectionId), cancellationToken));

    [HttpPost("{connectionId}/mappings")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromRoute] string connectionId, [FromBody] CreateDevelopmentRepositoryMappingRequest request, [FromServices] CreateMappingHandler handler, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(new CreateMappingCommand(connectionId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpDelete("mappings/{mappingId}")]
    public async Task<IActionResult> Delete([FromRoute] string mappingId, [FromQuery, BindRequired] long expectedVersion, [FromServices] DeleteMappingHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new DeleteMappingCommand(mappingId, expectedVersion, HttpContext.TraceIdentifier), cancellationToken);
        return NoContent();
    }
}
