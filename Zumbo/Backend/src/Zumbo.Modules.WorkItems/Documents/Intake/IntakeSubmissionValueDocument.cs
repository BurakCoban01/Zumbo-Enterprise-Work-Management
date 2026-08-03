using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class IntakeSubmissionValueDocument
{
    public string FieldKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
