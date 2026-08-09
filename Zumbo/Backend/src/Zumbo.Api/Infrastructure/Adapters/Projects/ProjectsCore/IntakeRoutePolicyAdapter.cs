using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class IntakeRoutePolicyAdapter(
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<OrganizationDocument> organizations,
    IDocumentRepository<BoardDocument> boards) : IIntakeRoutePolicy
{
    public async Task<IntakeRouteAuthorization> ValidateAsync(
        string organizationId,
        string projectId,
        string boardId,
        CancellationToken ct)
    {
        var project = await projects.SelectAsync(
            x => x.Id == projectId
                && x.OrganizationId == organizationId
                && !x.Archived,
            ct)
            ?? throw new NotFoundException(
                "INTAKE_ROUTE_NOT_FOUND",
                "Intake route was not found.");
        var organization = await organizations.SelectAsync(
            x => x.Id == project.OrganizationId || x.TenantKey == project.OrganizationId,
            ct)
            ?? throw new NotFoundException(
                "INTAKE_ROUTE_NOT_FOUND",
                "Intake route was not found.");
        if (!string.IsNullOrWhiteSpace(organization.Status)
            && organization.Status != OrganizationStatuses.Active)
        {
            throw new ConflictException(
                "INTAKE_ORGANIZATION_INACTIVE",
                "Intake forms require an active organization.");
        }

        var board = await boards.SelectAsync(
            x => x.Id == boardId
                && x.ProjectId == project.Id
                && !x.Archived,
            ct)
            ?? throw new NotFoundException(
                "INTAKE_ROUTE_NOT_FOUND",
                "Intake route was not found.");
        return new IntakeRouteAuthorization(
            project.OrganizationId,
            project.Id,
            board.Id);
    }
}
