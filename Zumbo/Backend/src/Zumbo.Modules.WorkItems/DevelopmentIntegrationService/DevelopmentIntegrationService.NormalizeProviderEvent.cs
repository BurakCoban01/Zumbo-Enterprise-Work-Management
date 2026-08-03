using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static NormalizedDevelopmentEvent NormalizeProviderEvent(
        DevelopmentRepositoryMappingDocument mapping,
        NormalizedDevelopmentEvent source) =>
        source with
        {
            Url = NormalizeLinkUrl(mapping.RepositoryUrl, source.Url),
            Status = NormalizeStatus(source.Status)
        };

}
