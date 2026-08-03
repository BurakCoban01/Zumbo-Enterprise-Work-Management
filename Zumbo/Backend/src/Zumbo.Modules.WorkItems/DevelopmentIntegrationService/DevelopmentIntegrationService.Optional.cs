using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static string? Optional(string? value, string label, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maximum)
            throw new ValidationException($"{label} cannot exceed {maximum} characters.");
        return normalized;
    }

}
