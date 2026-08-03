using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemCustomFieldValueDocument
{
    public string FieldKey { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public bool? BooleanValue { get; set; }
    public DateTimeOffset? DateValueUtc { get; set; }
    public string? OptionKey { get; set; }
    public bool Indexed { get; set; }
    public string SearchValue { get; set; } = string.Empty;
}
