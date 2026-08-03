using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class InitiativeStatusUpdateDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = InitiativeStatuses.Planned;
    public string Health { get; set; } = InitiativeHealth.NoUpdate;
    public int? Confidence { get; set; }
    public string Note { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
