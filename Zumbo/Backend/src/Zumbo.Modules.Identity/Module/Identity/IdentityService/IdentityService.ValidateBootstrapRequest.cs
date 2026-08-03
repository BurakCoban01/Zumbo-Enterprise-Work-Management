using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed partial class IdentityService{

    private bool ValidateBootstrapRequest(RegisterUserRequest request)
    {
        var options = bootstrapOptions.Value;
        var isBootstrapEmail = options.AdminEmails.Any(x =>
            x.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!isBootstrapEmail)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.BootstrapToken)
            || string.IsNullOrWhiteSpace(request.BootstrapToken)
            || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(options.BootstrapToken)),
                SHA256.HashData(Encoding.UTF8.GetBytes(request.BootstrapToken))))
        {
            throw new ForbiddenException("A valid bootstrap token is required for the configured administrator account.");
        }

        return true;
    }
}
