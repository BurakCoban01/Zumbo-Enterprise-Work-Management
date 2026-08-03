using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class RegisterUserHandler(IdentityService service)
{
    public Task<AuthResponse> HandleAsync(RegisterUserRequest request, CancellationToken ct) =>
        service.RegisterAsync(request, ct);
}
