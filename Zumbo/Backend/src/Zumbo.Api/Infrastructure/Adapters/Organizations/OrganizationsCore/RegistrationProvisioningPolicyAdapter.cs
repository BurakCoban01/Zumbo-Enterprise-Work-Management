using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Teams;
using Zumbo.SharedKernel;

public sealed class RegistrationProvisioningPolicyAdapter(
    IDocumentRepository<OrganizationDocument> organizations,
    IDocumentRepository<TeamDocument> teams,
    IOptions<RegistrationProvisioningOptions> options,
    IHostEnvironment environment,
    IClock clock) : IRegistrationProvisioningPolicy
{
    public async Task EnsureAllowedAsync(RegistrationProvisioningRequest request, CancellationToken ct)
    {
        var mode = options.Value.Mode.Trim();
        if (mode.Equals(RegistrationProvisioningModes.LocalDemo, StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("RegistrationProvisioning:Mode=LocalDemo is allowed only in Development.");
            }

            return;
        }

        if (!mode.Equals(RegistrationProvisioningModes.ProductionLike, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "RegistrationProvisioning:Mode must be ProductionLike or LocalDemo.");
        }

        var organizationId = request.OrganizationId.Trim().ToLowerInvariant();
        var organization = await organizations.SelectAsync(
            x => x.Id == organizationId || x.TenantKey == organizationId,
            ct);
        var organizationExists = organization is not null;
        var organizationActive = organization is null
            || string.IsNullOrWhiteSpace(organization.Status)
            || string.Equals(organization.Status, OrganizationStatuses.Active, StringComparison.Ordinal);

        if (request.IsBootstrap)
        {
            if ((organizationExists && organizationActive)
                || await organizations.CountByFilterAsync(cancellationToken: ct) == 0)
            {
                return;
            }

            if (organizationExists)
            {
                throw new ConflictException(
                    "REGISTRATION_ORGANIZATION_INACTIVE",
                    "The bootstrap organization is not active.");
            }

            throw new NotFoundException(
                "REGISTRATION_ORGANIZATION_NOT_FOUND",
                "The bootstrap organization does not exist.");
        }

        if (!organizationExists)
        {
            throw new NotFoundException(
                "REGISTRATION_ORGANIZATION_NOT_FOUND",
                "The registration organization does not exist.");
        }

        if (!organizationActive)
        {
            throw new ConflictException(
                "REGISTRATION_ORGANIZATION_INACTIVE",
                "Registration requires an active organization.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var now = clock.UtcNow;
        var invited = await teams.ExistsByFilterAsync(
            x => x.OrganizationId == organizationId
                && !x.Archived
                && x.Members.Any(member =>
                    member.Email == email
                    && member.Status == "Invited"
                    && member.InvitationExpiresAt > now),
            ct);
        if (!invited)
        {
            throw new ForbiddenException(
                "Production-like registration requires an active invitation for the requested organization.");
        }
    }
}
