namespace Zumbo.Modules.Identity;

public interface IRegistrationProvisioningPolicy
{
    Task EnsureAllowedAsync(RegistrationProvisioningRequest request, CancellationToken ct);
}
