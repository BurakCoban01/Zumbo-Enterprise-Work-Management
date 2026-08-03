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

    public async Task<LogoutResponse> LogoutAsync(LogoutRequest request, CancellationToken ct)
    {
        DocumentConcurrencyException? lastConflict = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await transactions.ExecuteAsync(
                    "Identity",
                    token => LogoutAttemptAsync(request, token),
                    ct);
            }
            catch (DocumentConcurrencyException conflict)
            {
                lastConflict = conflict;
            }
        }

        throw lastConflict!;
    }
}
