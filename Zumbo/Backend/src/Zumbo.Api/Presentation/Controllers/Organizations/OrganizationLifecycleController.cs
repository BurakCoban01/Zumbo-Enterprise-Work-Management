using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Organizations;

namespace Zumbo.Api.Presentation.Controllers.Organizations;

[ApiController]
[Route("/api/organizations/{organizationId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Organizations")]
[ZumboPermission(PermissionCatalog.OrganizationView)]
public sealed class OrganizationLifecycleController : ApiControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.OrganizationManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string organizationId, [FromBody] UpdateOrganizationRequest request, [FromServices] OrganizationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.UpdateAsync(organizationId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("ownership-transfer")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.OrganizationManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> TransferOwnership([FromRoute] string organizationId, [FromBody] TransferOrganizationOwnershipRequest request, [FromServices] OrganizationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.TransferOwnershipAsync(organizationId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("suspend")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.OrganizationManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Suspend([FromRoute] string organizationId, [FromBody] SuspendOrganizationRequest request, [FromServices] OrganizationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.SuspendAsync(organizationId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("archive")]
    [ZumboPermission(PermissionCatalog.OrganizationManage)]
    public async Task<IActionResult> Archive([FromRoute] string organizationId, [FromServices] OrganizationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ArchiveAsync(organizationId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("restore")]
    [ZumboPermission(PermissionCatalog.OrganizationManage)]
    public async Task<IActionResult> Restore([FromRoute] string organizationId, [FromServices] OrganizationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RestoreAsync(organizationId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpGet("members")]
    public async Task<IActionResult> ListMembers([FromRoute] string organizationId, [FromQuery] string? afterUserId, [FromQuery] int? pageSize, [FromServices] OrganizationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListMembersAsync(organizationId, afterUserId, pageSize ?? 50, cancellationToken));
}
