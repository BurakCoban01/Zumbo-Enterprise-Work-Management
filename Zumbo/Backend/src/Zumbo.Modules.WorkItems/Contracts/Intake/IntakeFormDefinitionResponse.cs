namespace Zumbo.Modules.WorkItems;

public sealed record IntakeFormDefinitionResponse(
    string AccessPolicy,
    string BoardId,
    string WorkItemType,
    string DefaultPriority,
    string ConfirmationMessage,
    IReadOnlyCollection<IntakeFieldDefinitionResponse> Fields,
    IntakeFieldMappingResponse Mapping);
