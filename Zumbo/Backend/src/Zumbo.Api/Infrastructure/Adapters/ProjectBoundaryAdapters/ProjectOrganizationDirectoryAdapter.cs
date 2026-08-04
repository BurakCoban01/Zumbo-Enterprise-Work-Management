using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class ProjectOrganizationDirectoryAdapter(
    IDocumentRepository<OrganizationDocument> organizations) : IProjectOrganizationDirectory
{
    public async Task EnsureActiveAsync(string organizationId, CancellationToken ct)
    {
        var organization = await organizations.SelectAsync(
            candidate => candidate.Id == organizationId || candidate.TenantKey == organizationId,
            ct)
            ?? throw new NotFoundException("ORGANIZATION_NOT_FOUND", "Project organization was not found.");
        if (!string.IsNullOrWhiteSpace(organization.Status)
            && organization.Status != OrganizationStatuses.Active)
        {
            throw new ConflictException("PROJECT_ORGANIZATION_INACTIVE", "Projects require an active organization.");
        }
    }
}
