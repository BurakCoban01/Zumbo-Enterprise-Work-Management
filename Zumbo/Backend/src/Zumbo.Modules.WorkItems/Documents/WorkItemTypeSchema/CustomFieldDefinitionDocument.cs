using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class CustomFieldDefinitionDocument
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = WorkItemFieldTypes.Text;
    public bool Required { get; set; }
    public bool Indexed { get; set; }
    public int? MaxLength { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public List<string> Options { get; set; } = [];
    public List<string> AppliesToIssueTypes { get; set; } = [];
    public int Position { get; set; }
}
