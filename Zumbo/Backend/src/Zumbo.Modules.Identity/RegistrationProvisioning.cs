namespace Zumbo.Modules.Identity;

public static class RegistrationProvisioningModes
{
    public const string ProductionLike = "ProductionLike";
    public const string LocalDemo = "LocalDemo";
}

public sealed class RegistrationProvisioningOptions
{
    public string Mode { get; init; } = RegistrationProvisioningModes.ProductionLike;
}

public sealed record RegistrationProvisioningRequest(
    string Email,
    string OrganizationId,
    bool IsBootstrap);

public interface IRegistrationProvisioningPolicy
{
    Task EnsureAllowedAsync(RegistrationProvisioningRequest request, CancellationToken ct);
}

internal sealed class LocalDemoRegistrationProvisioningPolicy : IRegistrationProvisioningPolicy
{
    internal static LocalDemoRegistrationProvisioningPolicy Instance { get; } = new();

    public Task EnsureAllowedAsync(RegistrationProvisioningRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
