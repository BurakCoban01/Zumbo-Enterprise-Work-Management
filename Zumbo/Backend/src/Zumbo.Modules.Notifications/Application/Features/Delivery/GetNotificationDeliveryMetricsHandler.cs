using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed class GetNotificationDeliveryMetricsHandler(NotificationService service)
{
    private GetNotificationDeliveryMetricsSlice? slice;

    public GetNotificationDeliveryMetricsHandler(
        IDocumentRepository<NotificationDocument> notifications,
        IClock clock)
        : this(null!) =>
        slice = new GetNotificationDeliveryMetricsSlice(notifications, clock);

    public Task<NotificationDeliveryMetrics> HandleAsync(
        GetNotificationDeliveryMetricsQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.GetDeliveryMetricsAsync(query.OrganizationId, ct);
}
