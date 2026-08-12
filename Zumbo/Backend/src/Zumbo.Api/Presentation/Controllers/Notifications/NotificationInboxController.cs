using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Notifications;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Presentation.Controllers.Notifications;

[ApiController]
[Route("/api/notifications")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Notifications")]
[ZumboPermission(PermissionCatalog.NotificationView)]
public sealed class NotificationInboxController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListMine(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool? unreadOnly,
        [FromServices] ListNotificationsHandler handler,
        [FromServices] ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new ListNotificationsQuery(
                currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required."),
                page ?? 1,
                pageSize ?? 50,
                unreadOnly ?? false),
            cancellationToken));

    [HttpGet("{userId}")]
    public async Task<IActionResult> ListForUser(
        [FromRoute] string userId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool? unreadOnly,
        [FromServices] ListNotificationsHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new ListNotificationsQuery(
                userId,
                page ?? 1,
                pageSize ?? 50,
                unreadOnly ?? false),
            cancellationToken));

    [HttpPatch("{notificationId}/read")]
    [ZumboPermission(PermissionCatalog.NotificationManage)]
    public async Task<IActionResult> MarkAsRead(
        [FromRoute] string notificationId,
        [FromServices] MarkNotificationAsReadHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new MarkNotificationAsReadCommand(notificationId),
            cancellationToken));
}
