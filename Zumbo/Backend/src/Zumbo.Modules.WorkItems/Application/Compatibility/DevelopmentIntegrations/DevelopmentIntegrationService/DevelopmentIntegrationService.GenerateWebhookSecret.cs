using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static string GenerateWebhookSecret(string provider)
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return provider == DevelopmentProviders.GitLab
            ? "whsec_" + Convert.ToBase64String(bytes)
            : "ghsec_" + Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
    }

}
