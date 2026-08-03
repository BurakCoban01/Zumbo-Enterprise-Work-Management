using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed class ListProjectsHandler(ProjectService service)
{
    private ListProjectsSlice? slice;

    public ListProjectsHandler(
        IDocumentRepository<ProjectDocument> projects,
        IProjectOrganizationDirectory organizationDirectory,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new ListProjectsSlice(projects, organizationDirectory, currentUser);
    }

    public Task<IReadOnlyList<ProjectResponse>> HandleAsync(ListProjectsQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ListAsync(query.OrganizationId, ct, query.Archived);
}
