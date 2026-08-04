using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    private static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type)) throw new ValidationException("Notification type is required.");
        var normalized = type.Trim();
        if (normalized.Length > 50) throw new ValidationException("Notification type cannot exceed 50 characters.");
        return normalized;
    }
}
