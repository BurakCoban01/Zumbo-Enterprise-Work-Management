namespace Zumbo.Modules.WorkItems;

public sealed record CreateIntakeFormRequest(
    string ProjectId,
    string Name,
    string? Description,
    IntakeFormDefinitionRequest Definition);
