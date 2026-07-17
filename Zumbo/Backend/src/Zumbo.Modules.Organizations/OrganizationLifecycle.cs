using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed partial class OrganizationService
{
    public async Task<OrganizationResponse> TransferOwnershipAsync(
        string organizationId,
        TransferOrganizationOwnershipRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var newOwnerUserId = NormalizeUserId(request.NewOwnerUserId);
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanTransferOwnership(organization);
        EnsureActive(organization);
        if (string.Equals(organization.OwnerUserId, newOwnerUserId, StringComparison.Ordinal))
        {
            throw new ConflictException("ORGANIZATION_OWNER_UNCHANGED", "The selected user already owns the organization.");
        }

        await memberDirectory.EnsureEligibleAsync(newOwnerUserId, organization.Id, ct);
        var oldOwnerUserId = organization.OwnerUserId;
        organization.OwnerUserId = newOwnerUserId;
        await SaveAsync(organization, ct);
        await audit.WriteAsync(
            "OrganizationOwnershipTransferred",
            organization.Id,
            oldOwnerUserId,
            newOwnerUserId,
            correlationId,
            ct);
        return ToResponse(organization);
    }

    public async Task<OrganizationResponse> SuspendAsync(
        string organizationId,
        SuspendOrganizationRequest request,
        string correlationId,
        CancellationToken ct)
    {
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        if (!string.Equals(organization.Status, OrganizationStatuses.Active, StringComparison.Ordinal))
        {
            throw new ConflictException("ORGANIZATION_NOT_ACTIVE", "Only an active organization can be suspended.");
        }

        organization.Status = OrganizationStatuses.Suspended;
        organization.SuspensionReason = NormalizeOptional(request.Reason, 500, "Suspension reason");
        organization.SuspendedAt = clock.UtcNow;
        await SaveAsync(organization, ct);
        await audit.WriteAsync(
            "OrganizationSuspended",
            organization.Id,
            OrganizationStatuses.Active,
            OrganizationStatuses.Suspended,
            correlationId,
            ct);
        return ToResponse(organization);
    }

    public async Task<OrganizationResponse> ArchiveAsync(
        string organizationId,
        string correlationId,
        CancellationToken ct)
    {
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        if (string.Equals(organization.Status, OrganizationStatuses.Archived, StringComparison.Ordinal))
        {
            throw new ConflictException("ORGANIZATION_ALREADY_ARCHIVED", "Organization is already archived.");
        }

        var oldStatus = organization.Status;
        organization.Status = OrganizationStatuses.Archived;
        organization.ArchivedAt = clock.UtcNow;
        organization.RetainUntil = clock.UtcNow.AddDays(Math.Clamp(lifecycle.ArchiveRetentionDays, 30, 3650));
        await SaveAsync(organization, ct);
        await audit.WriteAsync(
            "OrganizationArchived",
            organization.Id,
            oldStatus,
            $"{OrganizationStatuses.Archived}:{organization.RetainUntil:O}",
            correlationId,
            ct);
        return ToResponse(organization);
    }

    public async Task<OrganizationResponse> RestoreAsync(
        string organizationId,
        string correlationId,
        CancellationToken ct)
    {
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        if (string.Equals(organization.Status, OrganizationStatuses.Active, StringComparison.Ordinal))
        {
            throw new ConflictException("ORGANIZATION_ALREADY_ACTIVE", "Organization is already active.");
        }

        if (string.Equals(organization.Status, OrganizationStatuses.Archived, StringComparison.Ordinal)
            && organization.RetainUntil is { } retainUntil
            && retainUntil <= clock.UtcNow)
        {
            throw new ConflictException(
                "ORGANIZATION_RETENTION_EXPIRED",
                "The archived organization's restore retention period has expired.");
        }

        var oldStatus = organization.Status;
        organization.Status = OrganizationStatuses.Active;
        organization.SuspensionReason = null;
        organization.SuspendedAt = null;
        organization.ArchivedAt = null;
        organization.RetainUntil = null;
        await SaveAsync(organization, ct);
        await audit.WriteAsync(
            "OrganizationRestored",
            organization.Id,
            oldStatus,
            OrganizationStatuses.Active,
            correlationId,
            ct);
        return ToResponse(organization);
    }

    public async Task<OrganizationMemberPageResponse> ListMembersAsync(
        string organizationId,
        string? afterUserId,
        int pageSize,
        CancellationToken ct)
    {
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanView(organization);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);
        var cursor = string.IsNullOrWhiteSpace(afterUserId) ? null : afterUserId.Trim();
        var ordered = organization.Departments
            .SelectMany(department => department.Members.Select(member => new OrganizationMemberResponse(
                member.UserId,
                member.Position,
                department.Id,
                department.Name)))
            .OrderBy(member => member.UserId, StringComparer.Ordinal)
            .ThenBy(member => member.DepartmentId, StringComparer.Ordinal)
            .Where(member => cursor is null || string.CompareOrdinal(member.UserId, cursor) > 0)
            .Take(boundedPageSize + 1)
            .ToList();
        var hasNext = ordered.Count > boundedPageSize;
        var items = ordered.Take(boundedPageSize).ToList();
        return new OrganizationMemberPageResponse(
            items,
            hasNext ? items[^1].UserId : null,
            boundedPageSize);
    }

    private void EnsureCanTransferOwnership(OrganizationDocument organization)
    {
        var actorUserId = RequireCurrentUser();
        if (!IsSystemAdmin()
            && (!BelongsToTenant(organization)
                || !string.Equals(organization.OwnerUserId, actorUserId, StringComparison.Ordinal)))
        {
            throw new ForbiddenException("Only the current organization owner can transfer ownership.");
        }
    }

    private void EnsureCanView(OrganizationDocument organization)
    {
        RequireCurrentUser();
        if (!IsSystemAdmin() && !BelongsToTenant(organization))
        {
            throw new NotFoundException("ORGANIZATION_NOT_FOUND", "Organization was not found.");
        }
    }

    private static string NormalizeUserId(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ValidationException("New owner user id is required.");
        }

        var normalized = userId.Trim();
        if (normalized.Length > 128)
        {
            throw new ValidationException("User id cannot exceed 128 characters.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ValidationException($"{fieldName} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }
}
