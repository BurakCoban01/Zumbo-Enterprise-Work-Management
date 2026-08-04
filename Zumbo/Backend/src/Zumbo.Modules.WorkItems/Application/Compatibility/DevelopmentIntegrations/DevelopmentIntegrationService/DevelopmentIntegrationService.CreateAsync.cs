using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Development.Connections;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{
    public async Task<DevelopmentConnectionReceipt> CreateAsync(
        CreateDevelopmentConnectionRequest request,
        string correlationId,
        CancellationToken ct)
        => await createConnectionHandler.HandleAsync(
            new CreateConnectionCommand(request, correlationId),
            ct);

}
