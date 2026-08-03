using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public sealed record NotificationTypePreferenceRequest(
    string Type,
    bool InAppEnabled,
    bool EmailEnabled);
