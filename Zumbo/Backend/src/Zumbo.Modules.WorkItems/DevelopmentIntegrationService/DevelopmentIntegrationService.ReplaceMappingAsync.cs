using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private async Task ReplaceMappingAsync(
        DevelopmentRepositoryMappingDocument mapping,
        long expectedVersion,
        CancellationToken ct)
    {
        try
        {
            var result = await mappings.ReplaceByVersionAsync(
                item => item.Id == mapping.Id
                    && item.OrganizationId == mapping.OrganizationId,
                mapping,
                expectedVersion,
                ct);
            if (!result.Found) throw new NotFoundException(
                "DEVELOPMENT_REPOSITORY_MAPPING_NOT_FOUND",
                "Development repository mapping was not found.");
            mapping.Version = result.Version!.Value;
        }
        catch (DocumentConcurrencyException)
        {
            throw MappingConflict();
        }
    }

}
