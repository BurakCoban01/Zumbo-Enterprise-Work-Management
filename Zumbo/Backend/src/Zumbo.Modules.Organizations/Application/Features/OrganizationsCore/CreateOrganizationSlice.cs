using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

internal sealed class CreateOrganizationSlice(
    IDocumentRepository<OrganizationDocument> organizations,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser,
    IOrganizationAuditWriter audit)
{
    internal async Task<OrganizationResponse> HandleAsync(
        CreateOrganizationRequest request,
        string correlationId,
        CancellationToken ct)
    {
        CreateOrganizationValidator.Validate(request);
        var tenantKey = request.TenantKey.Trim().ToLowerInvariant();
        ValidateTenantKey(tenantKey);
        var actorUserId = RequireCurrentUser();
        if (!IsSystemAdmin()
            && !string.Equals(currentUser.OrganizationId, tenantKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("An organization can only be created for the authenticated user's tenant.");
        }

        await using var organizationLock = await AcquireLockAsync("organization:" + tenantKey, ct);
        var duplicate = await organizations.SelectAsync(
            document => document.Id == tenantKey || document.TenantKey == tenantKey,
            ct);
        if (duplicate is not null)
        {
            throw new ConflictException("TENANT_KEY_EXISTS", "Tenant key must be unique.");
        }

        var organization = new OrganizationDocument
        {
            Id = tenantKey,
            Name = request.Name.Trim(),
            TenantKey = tenantKey,
            OwnerUserId = actorUserId,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        await organizations.CreateAsync(organization, ct);
        await audit.WriteAsync(
            "OrganizationCreated",
            organization.Id,
            null,
            organization.Name,
            correlationId,
            ct);
        return OrganizationResponseMapper.ToResponse(organization);
    }

    private string RequireCurrentUser()
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        return currentUser.UserId;
    }

    private bool IsSystemAdmin() => PermissionCatalog.IsSystemAdministrator(currentUser.Roles);

    private static void ValidateTenantKey(string tenantKey)
    {
        if (tenantKey.Length is < 3 or > 64
            || tenantKey.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ValidationException(
                "Tenant key must be 3-64 characters and contain only letters, numbers, hyphens or underscores.");
        }
    }

    private async Task<IAsyncDisposable> AcquireLockAsync(string resource, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        return await distributedLockProvider.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException(
                "ORGANIZATION_RESOURCE_BUSY",
                "The organization is busy; retry the operation.");
    }
}
