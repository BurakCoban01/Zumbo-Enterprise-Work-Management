using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService(
    IDocumentRepository<NotificationDocument> notifications,
    IDocumentRepository<NotificationPreferenceDocument> preferences,
    INotificationUserDirectory userDirectory,
    IEmailNotificationSender emailSender,
    IOptions<EmailNotificationOptions> emailOptions,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser,
    IDurableMessageJitter? retryJitter = null)
{
}
