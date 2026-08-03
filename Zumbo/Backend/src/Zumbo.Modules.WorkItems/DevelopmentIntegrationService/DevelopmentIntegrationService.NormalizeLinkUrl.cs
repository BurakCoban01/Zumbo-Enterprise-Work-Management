using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static string NormalizeLinkUrl(string repositoryUrl, string value)
    {
        var normalized = NormalizeHttpsUrl(value, "Development link URL");
        if (!new Uri(normalized).Host.Equals(
                new Uri(repositoryUrl).Host,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                "Development link URL host must match the mapped repository.");
        }
        return normalized;
    }

}
