using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public sealed record NotificationUser(string Id, string OrganizationId, string Email, bool IsActive);
