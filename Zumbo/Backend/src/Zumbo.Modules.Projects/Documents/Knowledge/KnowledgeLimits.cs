using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public static class KnowledgeLimits
{
    public const int MaximumVersions = 50;
    public const int MaximumComments = 200;
    public const int MaximumContentCharacters = 40_000;
    public const int MaximumTags = 20;
    public const int MaximumWorkItemLinks = 50;
    public const int MaximumUserLinks = 30;
    public const int MaximumSearchDocuments = 1_000;
}
