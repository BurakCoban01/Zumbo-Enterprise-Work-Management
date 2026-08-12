namespace Zumbo.Modules.Identity;

public sealed record UpdatePermissionDefinitionRequest(
    string Label,
    string Description,
    string Category,
    int DisplayOrder,
    bool IsActive,
    long Version);
