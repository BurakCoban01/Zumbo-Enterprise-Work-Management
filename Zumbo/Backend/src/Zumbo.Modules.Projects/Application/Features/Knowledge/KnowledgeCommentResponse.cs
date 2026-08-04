using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record KnowledgeCommentResponse(
    string Id,
    string Body,
    string AuthorUserId,
    bool Resolved,
    string? ResolvedByUserId,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt);
