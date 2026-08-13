namespace Zumbo.Api.Presentation.Authorization;

public sealed record EndpointPermissionMetadata(string Permission, bool IsGlobal = false)
    : IEndpointPermissionMetadata;
