using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.Intake;

[ApiController]
[Route("/api/intake/forms/{formId}/submissions")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Intake")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class IntakeSubmissionsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromRoute] string formId, [FromQuery] string? state, [FromQuery] int? page, [FromQuery] int? pageSize, [FromServices] IntakeFormService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListSubmissionsAsync(formId, state, page ?? 1, pageSize ?? 20, cancellationToken));

    [HttpPost]
    [EnableRateLimiting("upload")]
    [ZumboPermission(PermissionCatalog.WorkItemCreate)]
    public async Task<IActionResult> Submit([FromRoute] string formId, [FromServices] IntakeSubmissionService service, CancellationToken cancellationToken) =>
        await SubmitAsync(formId, publicAccess: false, service, cancellationToken);

    [HttpPost("{submissionId}/triage")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Triage([FromRoute] string formId, [FromRoute] string submissionId, [FromBody] TriageIntakeSubmissionRequest request, [FromServices] IntakeFormService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.TriageAsync(formId, submissionId, request, HttpContext.TraceIdentifier, cancellationToken));

    private async Task<IActionResult> SubmitAsync(string identifier, bool publicAccess, IntakeSubmissionService service, CancellationToken cancellationToken)
    {
        await using var envelope = await IntakeSubmissionReader.ReadAsync(Request, cancellationToken);
        return CreatedEnvelopeResult(await service.SubmitAsync(
            identifier, publicAccess, envelope.Request, envelope.Attachments,
            Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty,
            HttpContext.TraceIdentifier, cancellationToken));
    }
}
