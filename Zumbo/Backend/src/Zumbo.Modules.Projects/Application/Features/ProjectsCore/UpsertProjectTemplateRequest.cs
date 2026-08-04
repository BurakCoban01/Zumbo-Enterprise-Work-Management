namespace Zumbo.Modules.Projects;
public sealed record UpsertProjectTemplateRequest(
    string Name,
    bool IsDefault,
    IReadOnlyCollection<string>? DefaultComponentNames = null);
