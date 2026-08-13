using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Notifications;

namespace Zumbo.Api.Presentation.Controllers.Notifications;

[ApiController]
[Route("/api/notifications/delivery")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Notifications")]
[ZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)]
public sealed class NotificationDeliveryOperationsController : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> GetStatus(
        [MinimalApiRequiredQuery] string organizationId,
        [FromServices] GetNotificationDeliveryMetricsHandler handler,
        CancellationToken cancellationToken)
    {
        if (!Request.Query.ContainsKey(nameof(organizationId)))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new EmptyResult();
        }

        return Ok(await handler.HandleAsync(
            new GetNotificationDeliveryMetricsQuery(organizationId),
            cancellationToken));
    }

    [HttpGet("dead-letters")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> ListDeadLetters(
        [MinimalApiRequiredQuery] string organizationId,
        [FromQuery] int? pageSize,
        [FromServices] ListNotificationDeadLettersHandler handler,
        CancellationToken cancellationToken)
    {
        if (!Request.Query.ContainsKey(nameof(organizationId)))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new EmptyResult();
        }

        return Ok(await handler.HandleAsync(
            new ListNotificationDeadLettersQuery(
                organizationId,
                Math.Clamp(pageSize ?? 20, 1, 50)),
            cancellationToken));
    }

    [HttpPost("{notificationId}/replay")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> Replay(
        [FromRoute] string notificationId,
        [MinimalApiRequiredQuery] string organizationId,
        [FromServices] ReplayNotificationDeadLetterHandler handler,
        [FromServices] INotificationAuditWriter audit,
        CancellationToken cancellationToken)
    {
        if (!Request.Query.ContainsKey(nameof(organizationId)))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new EmptyResult();
        }

        if (!await handler.HandleAsync(
                new ReplayNotificationDeadLetterCommand(organizationId, notificationId),
                cancellationToken))
        {
            return NotFound();
        }

        var correlationId = global::ApiEndpointResults.CorrelationId(HttpContext);
        await audit.WriteAsync(
            "NotificationDeliveryReplayed",
            notificationId,
            "DeadLetter",
            "Pending",
            correlationId,
            cancellationToken);
        return Ok();
    }
}
