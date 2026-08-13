using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Controllers;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Notifications;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Presentation.Controllers.Notifications;

[ApiController]
[Route("/api/notifications/preferences")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Notifications")]
[ZumboPermission(PermissionCatalog.NotificationView)]
public sealed class NotificationPreferencesController : ApiControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<NotificationPreferenceResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<NotificationPreferenceResponse>>> GetMine(
        [FromServices] GetNotificationPreferencesHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelope(await handler.HandleAsync(new GetNotificationPreferencesQuery(), cancellationToken));

    [HttpPut("me")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.NotificationManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> UpdateMine(
        [FromBody] UpdateNotificationPreferencesRequest request,
        [FromServices] UpdateNotificationPreferencesHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new UpdateNotificationPreferencesCommand(request),
            cancellationToken));
}
