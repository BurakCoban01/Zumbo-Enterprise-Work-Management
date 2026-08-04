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

    private Task<IAsyncDisposable> AcquireRegistrationLockAsync(CancellationToken ct) =>
        AcquireLockAsync("identity-registration", "IDENTITY_REGISTRATION_BUSY", ct);
}
