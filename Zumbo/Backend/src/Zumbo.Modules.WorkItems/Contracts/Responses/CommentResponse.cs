using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CommentResponse(
    string Id,
    string Body,
    string AuthorUserId,
    IReadOnlyCollection<string> Mentions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    IReadOnlyCollection<CommentRevisionResponse> History);
