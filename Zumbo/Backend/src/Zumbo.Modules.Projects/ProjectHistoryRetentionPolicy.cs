namespace Zumbo.Modules.Projects;

public static class ProjectHistoryRetentionPolicy
{
    public const int MaximumGoalStatusUpdates = 50;
    public const int MaximumKeyResultProgressUpdates = 50;
    public const int MaximumInitiativeStatusUpdates = 50;

    public static void RetainMostRecent<T>(List<T> items, int maximumRetainedItems)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRetainedItems);
        var excess = items.Count - maximumRetainedItems;
        if (excess > 0)
        {
            items.RemoveRange(maximumRetainedItems, excess);
        }
    }
}
