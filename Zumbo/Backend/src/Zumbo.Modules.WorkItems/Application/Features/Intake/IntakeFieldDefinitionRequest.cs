namespace Zumbo.Modules.WorkItems;

public sealed record IntakeFieldDefinitionRequest(
    string Key,
    string Label,
    string Type,
    bool Required = false,
    string? HelpText = null,
    IReadOnlyCollection<string>? Options = null);
