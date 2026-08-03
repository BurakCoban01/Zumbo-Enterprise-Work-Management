using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class CommentRevisionDocument
{
    public string Body { get; set; } = string.Empty;
    public string EditedByUserId { get; set; } = "system";
    public DateTimeOffset EditedAt { get; set; }
}
