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
[Route("/api/portfolios")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Portfolios")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class PortfolioCatalogController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? includeArchived, [FromQuery] int? page, [FromQuery] int? pageSize, [FromServices] ListPortfoliosHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ListPortfoliosQuery(includeArchived ?? false, page ?? 1, pageSize ?? 50), cancellationToken));

    [HttpGet("{portfolioId}")]
    public async Task<IActionResult> Get([FromRoute] string portfolioId, [FromQuery] bool? includeArchived, [FromServices] GetPortfolioHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetPortfolioQuery(portfolioId, includeArchived ?? false), cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromBody] SavePortfolioRequest request, [FromServices] SavePortfolioHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SavePortfolioCommand(null, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPut("{portfolioId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string portfolioId, [FromBody] SavePortfolioRequest request, [FromServices] SavePortfolioHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SavePortfolioCommand(portfolioId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpDelete("{portfolioId}")]
    public async Task<IActionResult> Archive([FromRoute] string portfolioId, [FromServices] ArchivePortfolioHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new ArchivePortfolioCommand(portfolioId, HttpContext.TraceIdentifier), cancellationToken);
        return OkEnvelopeResult(new { archived = true });
    }
}
