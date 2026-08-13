namespace Zumbo.Api.Presentation.Authorization;

public interface IEndpointPermissionMetadata
{
    string Permission { get; }

    bool IsGlobal { get; }
}
