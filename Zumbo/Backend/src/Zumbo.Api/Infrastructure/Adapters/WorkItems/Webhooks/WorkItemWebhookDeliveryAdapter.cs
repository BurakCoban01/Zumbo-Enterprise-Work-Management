using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Organizations;
using Zumbo.SharedKernel;

public sealed class WorkItemWebhookDeliveryAdapter(WorkItemWebhookService service) : IWorkItemWebhookDelivery
{
    public Task DeliverAsync(WorkItemWebhookEvent message, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Webhook delivery metadata is required.");

    public Task DeliverAsync(
        string sourceEventId,
        string organizationId,
        WorkItemWebhookEvent message,
        CancellationToken cancellationToken) =>
        service.QueueAsync(sourceEventId, organizationId, message, cancellationToken);
}
