using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed record NotificationResponse(
    string Id,
    string UserId,
    string Type,
    string Message,
    bool Read,
    string EmailStatus,
    DateTimeOffset CreatedAt);

public sealed record ListNotificationsQuery(
    string UserId,
    int Page = 1,
    int PageSize = 50,
    bool UnreadOnly = false);

public sealed class ListNotificationsValidator
{
    public static void Validate(ListNotificationsQuery query)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
        {
            throw new ValidationException("Notification page must be positive and page size must be between 1 and 100.");
        }
    }
}

public sealed class ListNotificationsHandler(NotificationService service)
{
    public Task<IReadOnlyList<NotificationResponse>> HandleAsync(ListNotificationsQuery query, CancellationToken ct) =>
        service.ListAsync(query.UserId, ct, query.Page, query.PageSize, query.UnreadOnly);
}

public sealed record MarkNotificationAsReadCommand(string NotificationId);
public sealed record MarkNotificationAsReadResponse(bool Read);

public sealed class MarkNotificationAsReadValidator
{
    public static void Validate(MarkNotificationAsReadCommand command) => ArgumentNullException.ThrowIfNull(command);
}

public sealed class MarkNotificationAsReadHandler(NotificationService service)
{
    public async Task<MarkNotificationAsReadResponse> HandleAsync(
        MarkNotificationAsReadCommand command,
        CancellationToken ct)
    {
        MarkNotificationAsReadValidator.Validate(command);
        await service.MarkAsReadAsync(command.NotificationId, ct);
        return new MarkNotificationAsReadResponse(true);
    }
}
