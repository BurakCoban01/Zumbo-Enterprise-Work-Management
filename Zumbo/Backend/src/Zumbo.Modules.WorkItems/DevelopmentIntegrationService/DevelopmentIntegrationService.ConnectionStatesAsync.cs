using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private async Task<Dictionary<string, bool>> ConnectionStatesAsync(
        string organizationId,
        IEnumerable<string> connectionIds,
        CancellationToken ct)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var connectionId in connectionIds.Distinct(StringComparer.Ordinal))
        {
            var connection = await connections.SelectAsync(
                item => item.Id == connectionId
                    && item.OrganizationId == organizationId,
                ct);
            result[connectionId] = connection?.IsConnected == true;
        }
        return result;
    }

}
