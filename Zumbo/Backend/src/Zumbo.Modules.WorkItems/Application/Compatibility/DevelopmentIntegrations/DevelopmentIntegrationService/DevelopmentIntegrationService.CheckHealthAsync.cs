using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Development.ProviderHealth;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentHealthResponse> CheckHealthAsync(
        string connectionId,
        string correlationId,
        CancellationToken ct)
        => await checkProviderHealthHandler.HandleAsync(
            new CheckProviderHealthCommand(connectionId, correlationId),
            ct);

}
