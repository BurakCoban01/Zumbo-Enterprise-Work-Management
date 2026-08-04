namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

public sealed record QueueDeliveryCommand(
    string SourceEventId,
    string OrganizationId,
    WorkItemWebhookEvent Message);
