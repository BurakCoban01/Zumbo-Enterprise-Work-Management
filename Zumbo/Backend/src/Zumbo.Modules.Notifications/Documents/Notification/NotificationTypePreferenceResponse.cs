using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public sealed record NotificationTypePreferenceResponse(
    string Type,
    bool InAppEnabled,
    bool EmailEnabled);
