using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Operations;

[ApiController]
[Route("/api/work-items/durable-messaging")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemDurableMessagingController : ControllerBase
{
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(
        [FromServices] IDurableEventOutbox outbox,
        [FromServices] IClock clock,
        CancellationToken cancellationToken) =>
        Ok(await outbox.GetMetricsAsync(clock.UtcNow, cancellationToken));

    [HttpGet("dead-letters")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> ListDeadLetters(
        [FromQuery] int? pageSize,
        [FromServices] IDurableEventOutbox outbox,
        CancellationToken cancellationToken) =>
        Ok(await outbox.ListDeadLettersAsync(Math.Clamp(pageSize ?? 20, 1, 50), cancellationToken));

    [HttpPost("dead-letter/{messageId}/replay")]
    public async Task<IActionResult> Replay(
        [FromRoute] string messageId,
        [FromServices] IDurableEventOutbox outbox,
        [FromServices] IWorkItemOperationsAuditWriter audit,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var replayed = await outbox.ReplayDeadLetterAsync(messageId, clock.UtcNow, cancellationToken);
        if (replayed)
        {
            await audit.WriteAsync(
                "DurableMessageReplayed",
                "DurableMessage",
                messageId,
                "DeadLetter",
                "Pending",
                HttpContext.TraceIdentifier,
                cancellationToken);
        }

        return Ok(new { replayed });
    }
}
