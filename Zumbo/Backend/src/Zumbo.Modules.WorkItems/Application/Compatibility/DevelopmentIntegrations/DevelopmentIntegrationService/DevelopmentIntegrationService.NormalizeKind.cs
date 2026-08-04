using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static string NormalizeKind(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var kind = DevelopmentLinkKinds.All.FirstOrDefault(
            item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return kind ?? throw new ValidationException(
            "Development link kind is not supported.");
    }

}
