namespace Zumbo.Modules.WorkItems;

public sealed record IntakeFieldDefinitionResponse(
    string Key,
    string Label,
    string Type,
    bool Required,
    string? HelpText,
    IReadOnlyCollection<string> Options);
