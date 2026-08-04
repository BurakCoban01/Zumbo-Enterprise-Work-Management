namespace Zumbo.Modules.Identity;

public sealed record RegistrationProvisioningRequest(
    string Email,
    string OrganizationId,
    bool IsBootstrap);
