using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed class ListProjectsHandler(ProjectService service)
{
    public Task<IReadOnlyList<ProjectResponse>> HandleAsync(ListProjectsQuery query, CancellationToken ct)
    {
        ListProjectsValidator.Validate(query);
        return service.ListAsync(query.OrganizationId, ct, query.Archived);
    }
}
