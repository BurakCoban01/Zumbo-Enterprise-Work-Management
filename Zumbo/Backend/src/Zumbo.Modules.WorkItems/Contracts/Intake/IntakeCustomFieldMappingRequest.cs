namespace Zumbo.Modules.WorkItems;

public sealed record IntakeCustomFieldMappingRequest(
    string IntakeFieldKey,
    string WorkItemFieldKey);
