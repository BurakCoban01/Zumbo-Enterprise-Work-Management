using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private static KnowledgeVersionDocument NormalizeVersion(
        string? title,
        string? contentMarkdown,
        IReadOnlyCollection<string>? tags,
        IReadOnlyCollection<string>? workItemIds,
        IReadOnlyCollection<string>? userIds,
        string? changeSummary,
        string authorUserId,
        int number,
        DateTimeOffset createdAt)
    {
        var content = NormalizeContent(contentMarkdown);
        return new KnowledgeVersionDocument
        {
            Number = number,
            Title = Required(title, "Knowledge document title", 160),
            ContentMarkdown = content,
            Tags = NormalizeLabels(tags, KnowledgeLimits.MaximumTags),
            WorkItemIds = NormalizeIds(
                workItemIds,
                KnowledgeLimits.MaximumWorkItemLinks,
                "Knowledge work-item link"),
            UserIds = NormalizeIds(
                userIds,
                KnowledgeLimits.MaximumUserLinks,
                "Knowledge user link"),
            ChangeSummary = Required(changeSummary, "Knowledge version summary", 500),
            AuthorUserId = authorUserId,
            CreatedAt = createdAt
        };
    }
}
