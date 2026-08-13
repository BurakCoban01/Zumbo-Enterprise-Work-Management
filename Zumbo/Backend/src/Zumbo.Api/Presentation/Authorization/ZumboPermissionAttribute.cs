using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.Api.Presentation.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class ZumboPermissionAttribute : Attribute, IEndpointPermissionMetadata
{
    public ZumboPermissionAttribute(string permission, bool isGlobal = false)
    {
        if (!PermissionCatalog.IsKnownEndpointPermission(permission))
        {
            throw new ArgumentException($"Endpoint permission '{permission}' is not catalogued.", nameof(permission));
        }

        Permission = permission;
        IsGlobal = isGlobal;
    }

    public string Permission { get; }

    public bool IsGlobal { get; }
}
