namespace Zumbo.Modules.Identity;

internal sealed class LocalDemoRegistrationProvisioningPolicy : IRegistrationProvisioningPolicy
{
    internal static LocalDemoRegistrationProvisioningPolicy Instance { get; } = new();

    public Task EnsureAllowedAsync(RegistrationProvisioningRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
