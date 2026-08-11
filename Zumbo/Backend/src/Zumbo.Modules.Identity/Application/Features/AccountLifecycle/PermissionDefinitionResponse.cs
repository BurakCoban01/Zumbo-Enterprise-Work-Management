namespace Zumbo.Modules.Identity;

public sealed record PermissionDefinitionResponse(
    string Key,
    string Label,
    string Description,
    string Category,
    string Scope,
    int DisplayOrder,
    bool IsActive,
    long Version);
