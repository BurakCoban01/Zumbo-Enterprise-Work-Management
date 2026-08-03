using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class BoardColumnWipProjectionDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string BoardId { get; set; } = string.Empty;
    public string ColumnId { get; set; } = string.Empty;
    public int ActiveCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
