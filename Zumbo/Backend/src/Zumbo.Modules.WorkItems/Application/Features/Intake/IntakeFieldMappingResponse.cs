namespace Zumbo.Modules.WorkItems;

public sealed record IntakeFieldMappingResponse(
    string TitleFieldKey,
    string? DescriptionFieldKey,
    string? PriorityFieldKey,
    string? DueDateFieldKey,
    IReadOnlyCollection<IntakeCustomFieldMappingResponse> CustomFields);
