using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Development.ProviderHealth;
using Zumbo.Modules.WorkItems.Application.Features.Development.Repositories;

namespace Zumbo.Api.Presentation.Controllers.Integrations;

[ApiController]
[Route("/api/integrations/development/{connectionId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Development integrations")]
[ZumboPermission(PermissionCatalog.IntegrationManage)]
[DurableTransaction("WorkItems")]
public sealed class DevelopmentProviderDiscoveryController : ApiControllerBase
{
    [HttpPost("health")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> CheckHealth([FromRoute] string connectionId, [FromServices] CheckProviderHealthHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new CheckProviderHealthCommand(connectionId, HttpContext.TraceIdentifier), cancellationToken));

    [HttpGet("repositories")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> ListRepositories([FromRoute] string connectionId, [FromServices] ListRepositoriesHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListRepositoriesQuery(connectionId), cancellationToken);
        return OkEnvelopeResult(new DevelopmentRepositoryPage(
            result.Items.Select(item => new DevelopmentRepositoryResponse(item.ExternalRepositoryId, item.Name, item.FullName, item.Url, item.DefaultBranch)).ToList(),
            result.Partial ? "Partial" : "Complete"));
    }
}
