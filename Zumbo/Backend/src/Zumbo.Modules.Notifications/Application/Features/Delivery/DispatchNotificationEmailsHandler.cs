using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed class DispatchNotificationEmailsHandler(NotificationService service)
{
    private DispatchNotificationEmailsSlice? slice;

    public DispatchNotificationEmailsHandler(
        IDocumentRepository<NotificationDocument> notifications,
        IEmailNotificationSender emailSender,
        IOptions<EmailNotificationOptions> emailOptions,
        IClock clock,
        IDurableMessageJitter? retryJitter = null)
        : this(null!) =>
        slice = new DispatchNotificationEmailsSlice(
            notifications,
            emailSender,
            emailOptions.Value,
            clock,
            new NotificationEmailRetryPolicy(retryJitter));

    public Task<int> HandleAsync(
        DispatchNotificationEmailsCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.DispatchPendingEmailsAsync(command.BatchSize, ct, command.WorkerId);
}
