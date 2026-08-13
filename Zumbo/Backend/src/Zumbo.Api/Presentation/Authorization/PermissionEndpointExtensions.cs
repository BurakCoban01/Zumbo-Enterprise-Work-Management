using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Api.Presentation.Authorization;

internal static class PermissionEndpointExtensions
{
    internal static TBuilder WithZumboPermission<TBuilder>(
        this TBuilder builder,
        string permission,
        bool isGlobal = false)
        where TBuilder : IEndpointConventionBuilder
    {
        if (!PermissionCatalog.IsKnownEndpointPermission(permission))
        {
            throw new InvalidOperationException($"Endpoint permission '{permission}' is not catalogued.");
        }

        return builder.WithMetadata(new EndpointPermissionMetadata(permission, isGlobal));
    }
}
