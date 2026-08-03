using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static string NormalizeProvider(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var provider = DevelopmentProviders.All.FirstOrDefault(
            item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return provider ?? throw new ValidationException(
            "Development provider must be GitHub or GitLab.");
    }

}
