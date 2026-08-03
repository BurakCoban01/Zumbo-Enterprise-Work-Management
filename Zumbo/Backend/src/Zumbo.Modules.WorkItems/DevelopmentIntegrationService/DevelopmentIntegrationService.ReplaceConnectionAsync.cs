using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private async Task ReplaceConnectionAsync(
        DevelopmentConnectionDocument connection,
        long expectedVersion,
        CancellationToken ct)
    {
        try
        {
            var result = await connections.ReplaceByVersionAsync(
                item => item.Id == connection.Id
                    && item.OrganizationId == connection.OrganizationId,
                connection,
                expectedVersion,
                ct);
            if (!result.Found) throw new NotFoundException(
                "DEVELOPMENT_CONNECTION_NOT_FOUND",
                "Development connection was not found.");
            connection.Version = result.Version!.Value;
        }
        catch (DocumentConcurrencyException)
        {
            throw ConnectionConflict();
        }
    }

}
