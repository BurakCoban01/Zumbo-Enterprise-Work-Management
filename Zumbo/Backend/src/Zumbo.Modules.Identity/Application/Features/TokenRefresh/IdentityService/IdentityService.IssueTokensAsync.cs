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

    private async Task<AuthResponse> IssueTokensAsync(
        UserDocument user,
        DateTimeOffset now,
        CancellationToken ct)
    {
        _ = await sessions.PurgeRetainedAsync(now, 100, ct);
        var created = CreateTokenResponse(user, now);
        await sessions.CreateAsync(created.Session, ct);
        return created.Response;
    }
}
