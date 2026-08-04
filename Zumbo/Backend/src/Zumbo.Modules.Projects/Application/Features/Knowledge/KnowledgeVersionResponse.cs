using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record KnowledgeVersionResponse(
    int Number,
    string Title,
    string ContentMarkdown,
    IReadOnlyCollection<string> Tags,
    IReadOnlyCollection<string> WorkItemIds,
    IReadOnlyCollection<string> UserIds,
    string ChangeSummary,
    string AuthorUserId,
    DateTimeOffset CreatedAt);
