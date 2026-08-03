using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static string RequireSecret(string value, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 16 or > 512
            || normalized.Any(char.IsWhiteSpace))
        {
            throw new ValidationException(
                $"{label} must contain between 16 and 512 non-whitespace characters.");
        }
        return normalized;
    }

}
