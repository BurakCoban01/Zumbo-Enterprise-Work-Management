using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;
using Zumbo.Modules.Organizations;
using Zumbo.SharedKernel;

public sealed class WorkItemWebhookDeliveryAdapter(WorkItemWebhookService service)
    : IWorkItemWebhookDelivery
{
    private readonly WorkItemWebhookService compatibilityService = service;
    private readonly QueueDeliveryHandler? handler;

    public WorkItemWebhookDeliveryAdapter(QueueDeliveryHandler handler)
        : this((WorkItemWebhookService)null!)
    {
        ArgumentNullException.ThrowIfNull(handler);
        this.handler = handler;
    }

    public Task DeliverAsync(WorkItemWebhookEvent message, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Webhook delivery metadata is required.");

    public Task DeliverAsync(
        string sourceEventId,
        string organizationId,
        WorkItemWebhookEvent message,
        CancellationToken cancellationToken)
    {
        if (handler is not null)
        {
            return handler.HandleAsync(
                new QueueDeliveryCommand(sourceEventId, organizationId, message),
                cancellationToken);
        }

        return compatibilityService.QueueAsync(
            sourceEventId,
            organizationId,
            message,
            cancellationToken);
    }
}
