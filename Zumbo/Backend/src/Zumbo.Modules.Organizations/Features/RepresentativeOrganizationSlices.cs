using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed record CreateOrganizationRequest(string Name, string TenantKey);

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

public sealed record DepartmentResponse(
    string Id,
    string Name,
    string? ParentDepartmentId,
    IReadOnlyCollection<DepartmentMemberResponse> Members);

public sealed record DepartmentMemberResponse(string UserId, string Position);

public sealed record OrganizationMemberResponse(
    string UserId,
    string Position,
    string DepartmentId,
    string DepartmentName);

public sealed record OrganizationMemberPageResponse(
    IReadOnlyList<OrganizationMemberResponse> Items,
    string? NextCursor,
    int PageSize);

public sealed class CreateOrganizationValidator
{
    public static void Validate(CreateOrganizationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.TenantKey))
        {
            throw new ValidationException("Organization name and tenant key are required.");
        }
    }
}

public sealed class CreateOrganizationHandler(OrganizationService service)
{
    public Task<OrganizationResponse> HandleAsync(
        CreateOrganizationRequest request,
        string correlationId,
        CancellationToken ct) =>
        service.CreateAsync(request, correlationId, ct);
}

public sealed record ListOrganizationsQuery;

public sealed class ListOrganizationsValidator
{
    public static void Validate(ListOrganizationsQuery query) => ArgumentNullException.ThrowIfNull(query);
}

public sealed class ListOrganizationsHandler(OrganizationService service)
{
    public Task<IReadOnlyList<OrganizationResponse>> HandleAsync(ListOrganizationsQuery query, CancellationToken ct)
    {
        ListOrganizationsValidator.Validate(query);
        return service.ListAsync(ct);
    }
}
