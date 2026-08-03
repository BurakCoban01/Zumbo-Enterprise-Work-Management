namespace Zumbo.Modules.WorkItems;

public sealed record IntakeFormDefinitionRequest(
    string AccessPolicy,
    string BoardId,
    string WorkItemType,
    string DefaultPriority,
    string ConfirmationMessage,
    IReadOnlyCollection<IntakeFieldDefinitionRequest> Fields,
    IntakeFieldMappingRequest Mapping);
