using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class IntakeCustomFieldMappingDocument
{
    public string IntakeFieldKey { get; set; } = string.Empty;
    public string WorkItemFieldKey { get; set; } = string.Empty;
}
