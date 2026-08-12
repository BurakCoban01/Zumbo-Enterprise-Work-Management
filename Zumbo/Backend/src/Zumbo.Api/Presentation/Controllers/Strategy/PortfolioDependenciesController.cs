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
[Route("/api/portfolios/{portfolioId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Portfolios")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class PortfolioDependenciesController : ApiControllerBase
{
    [HttpPost("dependencies")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> CreateDependency([FromRoute] string portfolioId, [FromBody] SavePortfolioDependencyRequest request, [FromServices] SavePortfolioDependencyHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SavePortfolioDependencyCommand(portfolioId, null, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPut("dependencies/{dependencyId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> UpdateDependency([FromRoute] string portfolioId, [FromRoute] string dependencyId, [FromBody] SavePortfolioDependencyRequest request, [FromServices] SavePortfolioDependencyHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SavePortfolioDependencyCommand(portfolioId, dependencyId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpGet("roadmap")]
    public async Task<IActionResult> GetRoadmap([FromRoute] string portfolioId, [FromServices] GetPortfolioRoadmapHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetPortfolioRoadmapQuery(portfolioId), cancellationToken));
}
