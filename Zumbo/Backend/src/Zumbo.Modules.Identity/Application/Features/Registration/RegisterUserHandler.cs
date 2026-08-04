using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class RegisterUserHandler(IdentityService service)
{
    private RegisterUserSlice? slice;

    public RegisterUserHandler(
        IUserRepository users,
        IRefreshSessionStore sessions,
        IDurableTransactionRunner transactions,
        IPasswordHasher passwordHasher,
        ITokenIssuer tokenIssuer,
        IOptions<JwtOptions> jwtOptions,
        IOptions<IdentityBootstrapOptions> bootstrapOptions,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IClock clock,
        IRegistrationProvisioningPolicy? registrationProvisioningPolicy = null,
        ISessionClientContext? sessionClientContext = null)
        : this(null!)
    {
        slice = new RegisterUserSlice(
            users,
            sessions,
            transactions,
            passwordHasher,
            tokenIssuer,
            jwtOptions,
            bootstrapOptions,
            distributedLockProvider,
            distributedLockOptions,
            clock,
            registrationProvisioningPolicy,
            sessionClientContext);
    }

    public Task<AuthResponse> HandleAsync(RegisterUserRequest request, CancellationToken ct) =>
        slice?.HandleAsync(request, ct) ?? service.RegisterAsync(request, ct);
}
