namespace Zumbo.Modules.Projects;

public sealed record ProjectTemplateResponse(
    string Id,
    string Name,
    bool IsDefault,
    bool Archived,
    IReadOnlyCollection<string> DefaultComponentNames);
