using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;

public sealed record OrganizationResponse(
    string Id,
    string Name,
    string TenantKey,
    string OwnerUserId,
    IReadOnlyCollection<DepartmentResponse> Departments,
    string Status = OrganizationStatuses.Active,
    DateTimeOffset? SuspendedAt = null,
    DateTimeOffset? ArchivedAt = null,
    DateTimeOffset? RetainUntil = null,
    long Version = 0) : IVersionedResource;
