using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed class CreateNotificationHandler(NotificationService service)
{
    private CreateNotificationSlice? slice;

    public CreateNotificationHandler(
        IDocumentRepository<NotificationDocument> notifications,
        IDocumentRepository<NotificationPreferenceDocument> preferences,
        INotificationUserDirectory userDirectory,
        IOptions<EmailNotificationOptions> emailOptions,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IClock clock)
        : this(null!) =>
        slice = new CreateNotificationSlice(
            notifications,
            preferences,
            userDirectory,
            emailOptions.Value,
            new NotificationCreationLockAccess(
                distributedLockProvider,
                distributedLockOptions.Value),
            clock);

    public Task HandleAsync(CreateNotificationCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.NotifyAsync(
            command.UserId,
            command.Type,
            command.Message,
            ct,
            command.DeduplicationKey);
}
