using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed class CreateProjectHandler(ProjectService service)
{
    private CreateProjectSlice? slice;

    public CreateProjectHandler(
        IDocumentRepository<ProjectDocument> projects,
        IProjectMemberDirectory memberDirectory,
        IProjectOrganizationDirectory organizationDirectory,
        IProjectAuditWriter audit,
        IClock clock,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new CreateProjectSlice(
            projects,
            memberDirectory,
            organizationDirectory,
            audit,
            clock,
            currentUser);
    }

    public Task<ProjectResponse> HandleAsync(
        CreateProjectRequest request,
        string correlationId,
        CancellationToken ct) =>
        slice?.HandleAsync(request, correlationId, ct)
        ?? service.CreateAsync(request, correlationId, ct);
}
