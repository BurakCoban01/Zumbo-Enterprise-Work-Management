using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private static void Apply(
        KnowledgeDocument document,
        KnowledgeVersionDocument version)
    {
        document.Title = version.Title;
        document.ContentMarkdown = version.ContentMarkdown;
        document.Tags = [.. version.Tags];
        document.WorkItemIds = [.. version.WorkItemIds];
        document.UserIds = [.. version.UserIds];
        document.CurrentContentVersion = version.Number;
    }
}
