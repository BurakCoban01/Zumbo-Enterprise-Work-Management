using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed partial class OrganizationService
{
    private readonly IDocumentRepository<OrganizationDocument> organizations;
    private readonly IOrganizationMemberDirectory memberDirectory;
    private readonly IDistributedLockProvider distributedLockProvider;
    private readonly IOptions<DistributedLockOptions> distributedLockOptions;
    private readonly IClock clock;
    private readonly ICurrentUser currentUser;
    private readonly IOrganizationAuditWriter audit;
    private readonly ExpectedVersionState expectedVersion;
    private readonly OrganizationLifecycleOptions lifecycle;

    public OrganizationService(
        IDocumentRepository<OrganizationDocument> organizations,
        IOrganizationMemberDirectory memberDirectory,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IClock clock,
        ICurrentUser currentUser,
        IOrganizationAuditWriter audit,
        IExpectedVersionAccessor? expectedVersions = null,
        IOptions<OrganizationLifecycleOptions>? lifecycleOptions = null)
    {
        this.organizations = organizations;
        this.memberDirectory = memberDirectory;
        this.distributedLockProvider = distributedLockProvider;
        this.distributedLockOptions = distributedLockOptions;
        this.clock = clock;
        this.currentUser = currentUser;
        this.audit = audit;
        expectedVersion = new ExpectedVersionState(expectedVersions);
        lifecycle = lifecycleOptions?.Value ?? new OrganizationLifecycleOptions();
    }

    public Task<OrganizationResponse> CreateAsync(
        CreateOrganizationRequest request,
        CancellationToken ct) =>
        CreateAsync(request, "none", ct);

    public async Task<OrganizationResponse> CreateAsync(
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
        return ToResponse(organization);
    }

    public async Task<IReadOnlyList<OrganizationResponse>> ListAsync(CancellationToken ct)
    {
        RequireCurrentUser();
        var tenantId = currentUser.OrganizationId;
        var result = IsSystemAdmin()
            ? await organizations.ListByFilterAsync(
                orderBy: document => document.Name,
                pageSize: 100,
                cancellationToken: ct)
            : await organizations.ListByFilterAsync(
                document => document.Id == tenantId || document.TenantKey == tenantId,
                document => document.Name,
                pageSize: 1,
                cancellationToken: ct);
        return result.Select(ToResponse).ToList();
    }

    public Task<OrganizationResponse> UpdateAsync(
        string organizationId,
        UpdateOrganizationRequest request,
        CancellationToken ct) =>
        UpdateAsync(organizationId, request, "none", ct);

    public async Task<OrganizationResponse> UpdateAsync(
        string organizationId,
        UpdateOrganizationRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var name = NormalizeName(request.Name, "Organization name");
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        EnsureActive(organization);
        EnsureTenantKeyUnchanged(organization, request.TenantKey);
        var oldName = organization.Name;
        organization.Name = name;
        await SaveAsync(organization, ct);
        await audit.WriteAsync(
            "OrganizationUpdated",
            organization.Id,
            oldName,
            organization.Name,
            correlationId,
            ct);
        return ToResponse(organization);
    }

    private async Task<OrganizationDocument> GetOrganization(string organizationId, CancellationToken ct) =>
        await organizations.SelectAsync(
            document => document.Id == organizationId || document.TenantKey == organizationId,
            ct)
        ?? throw new NotFoundException("ORGANIZATION_NOT_FOUND", "Organization was not found.");

    private void EnsureCanManage(OrganizationDocument organization)
    {
        var actorUserId = RequireCurrentUser();
        var canManage = IsSystemAdmin()
            || (BelongsToTenant(organization)
                && (string.Equals(organization.OwnerUserId, actorUserId, StringComparison.Ordinal)
                    || currentUser.Roles.Contains("OrganizationAdmin", StringComparer.OrdinalIgnoreCase)));
        if (!canManage)
        {
            throw new ForbiddenException("Organization management permission is required.");
        }
    }

    private bool BelongsToTenant(OrganizationDocument organization) =>
        string.Equals(currentUser.OrganizationId, organization.Id, StringComparison.OrdinalIgnoreCase)
        || string.Equals(currentUser.OrganizationId, organization.TenantKey, StringComparison.OrdinalIgnoreCase);

    private static void EnsureActive(OrganizationDocument organization)
    {
        if (!string.Equals(organization.Status, OrganizationStatuses.Active, StringComparison.Ordinal))
        {
            throw new ConflictException(
                "ORGANIZATION_NOT_ACTIVE",
                "Organization must be active for this operation.");
        }
    }

    private static void EnsureTenantKeyUnchanged(
        OrganizationDocument organization,
        string? requestedTenantKey)
    {
        if (requestedTenantKey is null)
        {
            return;
        }

        var normalized = requestedTenantKey.Trim().ToLowerInvariant();
        if (!string.Equals(normalized, organization.TenantKey, StringComparison.Ordinal))
        {
            throw new ConflictException(
                "TENANT_KEY_IMMUTABLE",
                "Organization tenant key cannot be changed after creation.");
        }
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

    private static string NormalizeName(string? name, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(fieldName + " is required.");
        }

        var normalized = name.Trim();
        if (normalized.Length > 120)
        {
            throw new ValidationException(fieldName + " cannot exceed 120 characters.");
        }

        return normalized;
    }

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

    private async Task SaveAsync(OrganizationDocument organization, CancellationToken ct)
    {
        organization.UpdatedAt = clock.UtcNow;
        var result = await organizations.ReplaceByVersionAsync(
            document => document.Id == organization.Id,
            organization,
            expectedVersion.Consume(organization.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException("ORGANIZATION_NOT_FOUND", "Organization was not found.");
        }

        organization.Version = result.Version!.Value;
    }

    private static OrganizationResponse ToResponse(OrganizationDocument organization) =>
        new(
            organization.Id,
            organization.Name,
            organization.TenantKey,
            organization.OwnerUserId,
            organization.Departments.Select(department =>
                new DepartmentResponse(
                    department.Id,
                    department.Name,
                    department.ParentDepartmentId,
                    department.Members.Select(member =>
                        new DepartmentMemberResponse(member.UserId, member.Position)).ToList())).ToList(),
            organization.Status,
            organization.SuspendedAt,
            organization.ArchivedAt,
            organization.RetainUntil,
            organization.Version);
}
