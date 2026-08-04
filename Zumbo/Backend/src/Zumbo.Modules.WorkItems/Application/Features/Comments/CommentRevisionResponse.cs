using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CommentRevisionResponse(string Body, string EditedByUserId, DateTimeOffset EditedAt);
