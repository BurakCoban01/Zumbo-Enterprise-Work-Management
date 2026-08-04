using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static string NormalizeStatus(string value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "open" => "Open",
            "merged" => "Merged",
            "closed" => "Closed",
            "success" => "Success",
            "failed" => "Failed",
            "pending" => "Pending",
            "running" => "Running",
            "pushed" => "Pushed",
            "unknown" or "" or null => "Unknown",
            _ => throw new ValidationException("Development status is not supported.")
        };

}
