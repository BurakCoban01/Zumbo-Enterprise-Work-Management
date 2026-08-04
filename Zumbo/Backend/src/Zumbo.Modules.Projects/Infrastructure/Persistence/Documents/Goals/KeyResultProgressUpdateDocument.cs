using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class KeyResultProgressUpdateDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public decimal PreviousValue { get; set; }
    public decimal CurrentValue { get; set; }
    public int? Confidence { get; set; }
    public string Note { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
