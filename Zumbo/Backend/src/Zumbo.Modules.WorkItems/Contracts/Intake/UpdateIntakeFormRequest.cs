namespace Zumbo.Modules.WorkItems;

public sealed record UpdateIntakeFormRequest(
    string Name,
    string? Description,
    IntakeFormDefinitionRequest Definition);
