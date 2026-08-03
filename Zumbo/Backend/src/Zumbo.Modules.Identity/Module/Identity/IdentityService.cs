using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed partial class IdentityService(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IDurableTransactionRunner transactions,
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    IOptions<JwtOptions> jwtOptions,
    IOptions<LoginSecurityOptions> loginSecurityOptions,
    IOptions<IdentityBootstrapOptions> bootstrapOptions,
    IOptions<PasswordResetOptions> passwordResetOptions,
    IPasswordResetNotifier passwordResetNotifier,
    IMfaSecretProtector mfaSecretProtector,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser,
    IRegistrationProvisioningPolicy? registrationProvisioningPolicy = null,
    ISessionClientContext? sessionClientContext = null,
    IIdentityAuditWriter? audit = null)
{
}
