using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public sealed record NotificationDeliveryMetrics(
    string OrganizationId,
    long Pending,
    long Processing,
    long Sent,
    long DeadLetter,
    long Disabled,
    DateTimeOffset? OldestPendingAt,
    DateTimeOffset CapturedAt);
