using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

public sealed class EndpointPermissionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IdentityPermissionService permissions)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().LastOrDefault();
        var apiKeyIdentity = context.User.Identities.FirstOrDefault(identity =>
            identity.IsAuthenticated
            && identity.AuthenticationType == ZumboAuthenticationSchemes.ApiKey);
        if (apiKeyIdentity is not null)
        {
            var scopes = apiKeyIdentity.FindAll("scope").Select(claim => claim.Value);
            if (metadata is null || !ApiKeyScopes.GrantsPermission(scopes, metadata.Permission))
            {
                throw new ForbiddenException("API key scope does not grant access to this endpoint.");
            }
        }

        if (metadata is { IsGlobal: true }
            && !await permissions.HasPermissionAsync(metadata.Permission, context.RequestAborted))
        {
            throw new ForbiddenException($"Permission '{metadata.Permission}' is required.");
        }

        await next(context);
    }
}
