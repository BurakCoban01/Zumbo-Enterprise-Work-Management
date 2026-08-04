namespace Zumbo.Modules.WorkItems;

public sealed record PublishedIntakeFormResponse(
    string FormId,
    int Version,
    string Name,
    string Description,
    string AccessPolicy,
    string ConfirmationMessage,
    IReadOnlyCollection<IntakeFieldDefinitionResponse> Fields);
