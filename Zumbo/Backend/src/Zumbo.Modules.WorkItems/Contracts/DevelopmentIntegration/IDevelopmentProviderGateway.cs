namespace Zumbo.Modules.WorkItems;

public interface IDevelopmentProviderGateway
{
    Task ValidateBaseUrlAsync(
        string provider,
        string baseUrl,
        CancellationToken ct);

    Task<DevelopmentProviderProbeResult> ProbeAsync(
        string provider,
        string baseUrl,
        string accessToken,
        CancellationToken ct);

    Task<DevelopmentProviderRepositoryResult> ListRepositoriesAsync(
        string provider,
        string baseUrl,
        string accessToken,
        int maximumItems,
        CancellationToken ct);
}
