using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class IntakeFormDefinitionDocument
{
    public string AccessPolicy { get; set; } = IntakeAccessPolicies.Internal;
    public string BoardId { get; set; } = string.Empty;
    public string WorkItemType { get; set; } = "Task";
    public string DefaultPriority { get; set; } = "Medium";
    public string ConfirmationMessage { get; set; } = "Your request has been received.";
    public List<IntakeFieldDefinitionDocument> Fields { get; set; } = [];
    public IntakeFieldMappingDocument Mapping { get; set; } = new();
}
