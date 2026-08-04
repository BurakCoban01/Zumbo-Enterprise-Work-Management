using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

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
