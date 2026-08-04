using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentProviderRepositoryResult> ListProviderRepositoriesAsync(
        string connectionId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        EnsureConnected(connection);
        return await providerGateway.ListRepositoriesAsync(
            connection.Provider,
            connection.BaseUrl,
            credentialProtector.Unprotect(connection.CredentialProtected),
            DevelopmentIntegrationLimits.MaximumProviderRepositories,
            ct);
    }

}
