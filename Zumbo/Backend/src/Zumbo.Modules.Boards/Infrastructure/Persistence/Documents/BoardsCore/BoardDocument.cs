using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Boards;

public sealed class BoardDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Kanban";
    public bool Archived { get; set; }
    public long Version { get; set; }
    public int WorkflowMappingVersion { get; set; }
    public string SwimlaneMode { get; set; } = "None";
    public List<BoardColumnDocument> Columns { get; set; } = [];
    public List<BoardViewDocument> Views { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
