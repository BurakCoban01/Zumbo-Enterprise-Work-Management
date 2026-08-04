using Zumbo.BuildingBlocks.Application.Security;

public sealed record EndpointPermissionMetadata(string Permission, bool IsGlobal = false);
