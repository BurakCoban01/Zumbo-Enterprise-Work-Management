using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static string NormalizeRepositoryUrl(
        DevelopmentConnectionDocument connection,
        string value)
    {
        var normalized = NormalizeHttpsUrl(value, "Repository URL");
        var providerHost = new Uri(connection.BaseUrl).Host;
        var repositoryHost = new Uri(normalized).Host;
        var allowed = repositoryHost.Equals(providerHost, StringComparison.OrdinalIgnoreCase)
            || connection.Provider == DevelopmentProviders.GitHub
            && providerHost.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            && repositoryHost.Equals("github.com", StringComparison.OrdinalIgnoreCase);
        if (!allowed)
            throw new ValidationException(
                "Repository URL host must match the configured development provider.");
        return normalized;
    }

}
