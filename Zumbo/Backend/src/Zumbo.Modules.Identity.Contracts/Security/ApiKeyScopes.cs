namespace Zumbo.BuildingBlocks.Application.Security;

public static class ApiKeyScopes
{
    public const string Full = "api:full";
    public const string PermissionPrefix = "permission:";

    public static bool IsValid(string scope) =>
        scope.Equals(Full, StringComparison.OrdinalIgnoreCase)
        || scope.StartsWith(PermissionPrefix, StringComparison.OrdinalIgnoreCase)
        && PermissionCatalog.IsKnownEndpointPermission(scope[PermissionPrefix.Length..]);

    public static bool GrantsPermission(IEnumerable<string> scopes, string permission) =>
        scopes.Any(scope =>
            scope.Equals(Full, StringComparison.OrdinalIgnoreCase)
            || scope.Equals(PermissionPrefix + permission, StringComparison.OrdinalIgnoreCase));
}
