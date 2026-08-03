using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class IntakeFieldMappingDocument
{
    public string TitleFieldKey { get; set; } = string.Empty;
    public string? DescriptionFieldKey { get; set; }
    public string? PriorityFieldKey { get; set; }
    public string? DueDateFieldKey { get; set; }
    public List<IntakeCustomFieldMappingDocument> CustomFields { get; set; } = [];
}
