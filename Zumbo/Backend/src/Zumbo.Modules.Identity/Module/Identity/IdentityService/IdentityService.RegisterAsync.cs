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
    public async Task<AuthResponse> RegisterAsync(RegisterUserRequest request, CancellationToken ct)
    {
        RegisterUserValidator.Validate(request);

        await using var registrationLock = await AcquireRegistrationLockAsync(ct);

        var isBootstrap = ValidateBootstrapRequest(request);
        await (registrationProvisioningPolicy ?? LocalDemoRegistrationProvisioningPolicy.Instance)
            .EnsureAllowedAsync(
                new RegistrationProvisioningRequest(
                    request.Email.Trim().ToLowerInvariant(),
                    request.OrganizationId.Trim().ToLowerInvariant(),
                    isBootstrap),
                ct);

        if (isBootstrap && await users.HasSystemAdminAsync(ct))
        {
            throw new ConflictException(
                "BOOTSTRAP_ALREADY_COMPLETED",
                "System administrator bootstrap has already been completed.");
        }

        if (await users.GetByUsernameOrEmailAsync(request.Username, ct) is not null
            || await users.GetByUsernameOrEmailAsync(request.Email, ct) is not null)
        {
            throw new ConflictException("USER_ALREADY_EXISTS", "Username or email is already used.");
        }

        var now = clock.UtcNow;
        var roles = isBootstrap ? new List<string> { "User", "SystemAdmin" } : ["User"];
        var user = new UserDocument
        {
            Username = request.Username.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            OrganizationId = request.OrganizationId.Trim().ToLowerInvariant(),
            PasswordHash = passwordHasher.Hash(request.Password),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            Roles = roles
        };

        return await transactions.ExecuteAsync(
            "Identity",
            async token =>
            {
                await users.AddAsync(user, token);
                return await IssueTokensAsync(user, now, token);
            },
            ct);
    }
}
