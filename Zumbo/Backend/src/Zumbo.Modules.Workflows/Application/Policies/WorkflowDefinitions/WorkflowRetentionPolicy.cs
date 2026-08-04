namespace Zumbo.Modules.Workflows;

public static class WorkflowRetentionPolicy
{
    public const int MaximumPublishedVersions = 25;

    public static void RetainPublishedVersions(List<WorkflowVersionDocument> versions)
    {
        versions.Sort((left, right) => left.Number.CompareTo(right.Number));
        var excess = versions.Count - MaximumPublishedVersions;
        if (excess > 0)
        {
            versions.RemoveRange(0, excess);
        }
    }
}
