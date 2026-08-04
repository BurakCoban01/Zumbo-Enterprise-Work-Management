using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

public sealed class WorkflowAutomationDocument
{
    public string Action { get; set; } = string.Empty;
    public string? Value { get; set; }
}
