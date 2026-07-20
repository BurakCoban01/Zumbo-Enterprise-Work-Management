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

public sealed class BoardViewDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public bool IsShared { get; set; }
    public string SwimlaneMode { get; set; } = "None";
    public BoardFilterDocument Filter { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BoardFilterDocument
{
    public string? AssigneeUserId { get; set; }
    public string? TeamId { get; set; }
    public List<string> Statuses { get; set; } = [];
    public List<string> Priorities { get; set; } = [];
    public List<string> Labels { get; set; } = [];
    public string? Text { get; set; }
}

public sealed class BoardColumnDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Todo";
    public int Position { get; set; }
    public int? WipLimit { get; set; }
    public List<string> StatusNames { get; set; } = [];
}
