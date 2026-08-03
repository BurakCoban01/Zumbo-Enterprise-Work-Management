namespace Zumbo.Modules.WorkItems;

public sealed record IntakeFieldMappingRequest(
    string TitleFieldKey,
    string? DescriptionFieldKey = null,
    string? PriorityFieldKey = null,
    string? DueDateFieldKey = null,
    IReadOnlyCollection<IntakeCustomFieldMappingRequest>? CustomFields = null);
