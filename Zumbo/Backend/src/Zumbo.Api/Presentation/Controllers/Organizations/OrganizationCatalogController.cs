using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Organizations;

namespace Zumbo.Api.Presentation.Controllers.Organizations;

[ApiController]
[Route("/api/organizations")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Organizations")]
[ZumboPermission(PermissionCatalog.OrganizationView)]
public sealed class OrganizationCatalogController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromServices] ListOrganizationsHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ListOrganizationsQuery(), cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.OrganizationManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrganizationRequest request,
        [FromServices] CreateOrganizationHandler handler,
        CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(
            request, HttpContext.TraceIdentifier, cancellationToken));
}
