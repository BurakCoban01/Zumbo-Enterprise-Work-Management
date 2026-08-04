using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed class ListOrganizationsHandler(OrganizationService service)
{
    private ListOrganizationsSlice? slice;

    public ListOrganizationsHandler(
        IDocumentRepository<OrganizationDocument> organizations,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new ListOrganizationsSlice(organizations, currentUser);
    }

    public Task<IReadOnlyList<OrganizationResponse>> HandleAsync(
        ListOrganizationsQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct) ?? service.ListAsync(ct);
}
