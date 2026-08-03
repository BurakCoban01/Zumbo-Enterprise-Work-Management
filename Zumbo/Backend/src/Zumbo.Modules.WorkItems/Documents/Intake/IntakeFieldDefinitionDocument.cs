using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class IntakeFieldDefinitionDocument
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = IntakeFieldTypes.Text;
    public bool Required { get; set; }
    public string? HelpText { get; set; }
    public List<string> Options { get; set; } = [];
}
