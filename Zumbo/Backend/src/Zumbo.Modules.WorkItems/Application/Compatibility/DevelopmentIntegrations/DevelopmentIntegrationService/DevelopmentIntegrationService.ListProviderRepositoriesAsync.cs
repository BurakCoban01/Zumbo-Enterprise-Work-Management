using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Development.Repositories;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentProviderRepositoryResult> ListProviderRepositoriesAsync(
        string connectionId,
        CancellationToken ct)
        => await listRepositoriesHandler.HandleAsync(
            new ListRepositoriesQuery(connectionId),
            ct);

}
