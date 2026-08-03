using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public sealed record NotificationDeadLetterSummary(
    string Id,
    string Type,
    int Attempts,
    DateTimeOffset DeadLetteredAt);
