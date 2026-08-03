using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static string NormalizeBaseUrl(string provider, string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? provider == DevelopmentProviders.GitHub
                ? "https://api.github.com"
                : "https://gitlab.com/api/v4"
            : value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || !string.IsNullOrWhiteSpace(uri.Query)
            || !string.IsNullOrWhiteSpace(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ValidationException(
                "Development provider base URL must be an absolute HTTP(S) URL without credentials, query or fragment.");
        }
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

}
