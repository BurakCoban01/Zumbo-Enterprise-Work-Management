using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    private static string NormalizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ValidationException("Notification message is required.");
        var normalized = message.Trim();
        if (normalized.Length > 2000) throw new ValidationException("Notification message cannot exceed 2000 characters.");
        return normalized;
    }
}
