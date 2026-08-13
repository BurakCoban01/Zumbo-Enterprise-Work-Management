using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.Intake;

[ApiController]
[Route("/api/intake/public/forms/{publicId}")]
[EnableRateLimiting("api")]
[Tags("PublicIntake")]
[DurableTransaction("WorkItems")]
public sealed class PublicIntakeController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromRoute] string publicId, [FromServices] IntakeFormService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.GetPublishedAsync(publicId, publicAccess: true, cancellationToken));

    [HttpPost("submissions")]
    [EnableRateLimiting("intake-public")]
    public async Task<IActionResult> Submit([FromRoute] string publicId, [FromServices] IntakeSubmissionService service, CancellationToken cancellationToken)
    {
        await using var envelope = await IntakeSubmissionReader.ReadAsync(Request, cancellationToken);
        return CreatedEnvelopeResult(await service.SubmitAsync(
            publicId, publicAccess: true, envelope.Request, envelope.Attachments,
            Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty,
            HttpContext.TraceIdentifier, cancellationToken));
    }
}
