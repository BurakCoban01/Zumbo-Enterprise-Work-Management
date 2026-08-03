using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class WorkItemBulkJobDurableHandler(
    WorkItemBulkJobProcessor processor,
    IUserRepository users,
    IHttpContextAccessor httpContextAccessor) : IDurableEventHandler
{
    public string ConsumerName => "work-item-bulk-job-v1";
    public string EventType => WorkItemDurableEventTypes.BulkJob;

    public async Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken)
    {
        var payload = DurablePayload.Read<WorkItemBulkJobDueEvent>(message);
        var actor = await users.GetByIdAsync(payload.RequestedByUserId, cancellationToken);
        var roles = actor is { IsActive: true } ? actor.Roles : [];
        var previous = httpContextAccessor.HttpContext;
        var context = new DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
            [
                new(System.Security.Claims.ClaimTypes.NameIdentifier, payload.RequestedByUserId),
                new("organizationId", payload.OrganizationId),
                .. roles.Select(role => new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Role, role))
            ], "BulkJob"));
        context.TraceIdentifier = message.CorrelationId;
        httpContextAccessor.HttpContext = context;
        try
        {
            await processor.ProcessAsync(payload, cancellationToken);
        }
        finally
        {
            httpContextAccessor.HttpContext = previous;
        }
    }
}
