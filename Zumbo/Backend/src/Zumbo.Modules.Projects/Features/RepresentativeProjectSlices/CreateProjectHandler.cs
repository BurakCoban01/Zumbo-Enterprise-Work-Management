using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed class CreateProjectHandler(ProjectService service)
{
    public Task<ProjectResponse> HandleAsync(CreateProjectRequest request, string correlationId, CancellationToken ct) =>
        service.CreateAsync(request, correlationId, ct);
}
