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

    private async Task RecordFailedLoginAsync(UserDocument user, DateTimeOffset now, CancellationToken ct)
    {
        user.FailedLoginCount++;
        var securityOptions = loginSecurityOptions.Value;
        if (user.FailedLoginCount >= Math.Clamp(securityOptions.MaxFailedAttempts, 3, 20))
        {
            user.LockedUntil = now.AddMinutes(Math.Clamp(securityOptions.LockoutMinutes, 1, 1440));
        }

        await users.UpdateAsync(user, ct);
    }
}
