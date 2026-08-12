using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Organizations;

namespace Zumbo.Api.Presentation.Controllers.Organizations;

[ApiController]
[Route("/api/organizations/{organizationId}/departments")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Organizations")]
[ZumboPermission(PermissionCatalog.OrganizationManage)]
public sealed class OrganizationDepartmentsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromRoute] string organizationId, [FromBody] CreateDepartmentRequest request, [FromServices] OrganizationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.CreateDepartmentAsync(organizationId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPut("{departmentId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string organizationId, [FromRoute] string departmentId, [FromBody] UpdateDepartmentRequest request, [FromServices] OrganizationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.UpdateDepartmentAsync(organizationId, departmentId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpDelete("{departmentId}")]
    public async Task<IActionResult> Delete([FromRoute] string organizationId, [FromRoute] string departmentId, [FromServices] OrganizationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.DeleteDepartmentAsync(organizationId, departmentId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("{departmentId}/members")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> AssignMember([FromRoute] string organizationId, [FromRoute] string departmentId, [FromBody] AssignDepartmentMemberRequest request, [FromServices] OrganizationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.AssignMemberAsync(organizationId, departmentId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPatch("{departmentId}/members/{userId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> UpdateMember([FromRoute] string organizationId, [FromRoute] string departmentId, [FromRoute] string userId, [FromBody] UpdateDepartmentMemberRequest request, [FromServices] OrganizationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.UpdateMemberPositionAsync(organizationId, departmentId, userId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpDelete("{departmentId}/members/{userId}")]
    public async Task<IActionResult> RemoveMember([FromRoute] string organizationId, [FromRoute] string departmentId, [FromRoute] string userId, [FromServices] OrganizationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RemoveMemberAsync(organizationId, departmentId, userId, HttpContext.TraceIdentifier, cancellationToken));
}
