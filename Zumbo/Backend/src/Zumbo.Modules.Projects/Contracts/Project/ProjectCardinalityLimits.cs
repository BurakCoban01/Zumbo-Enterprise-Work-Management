namespace Zumbo.Modules.Projects;

public static class ProjectCardinalityLimits
{
    public const int MaximumMembers = 500;
    public const int MaximumTeams = 100;
    public const int MaximumTemplates = 100;
    public const int MaximumComponents = 500;
    public const int MaximumVersions = 500;
    public const int MaximumReleases = 500;
    public const int MaximumMilestones = 500;
    public const int MaximumSerializedBytes = 2 * 1024 * 1024;

    internal static void EnsureCanGrow(
        int currentCount,
        int maximum,
        string code,
        string collectionName)
    {
        if (currentCount >= maximum)
        {
            throw new Zumbo.SharedKernel.ConflictException(
                code,
                $"Project {collectionName} cannot contain more than {maximum} entries.");
        }
    }
}
