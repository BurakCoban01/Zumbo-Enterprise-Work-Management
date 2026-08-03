using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    public async Task<PortfolioResponse> SaveInitiativeAsync(
        string portfolioId,
        string? initiativeId,
        SaveInitiativeRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var portfolio = await GetDocumentAsync(portfolioId, includeArchived: false, ct);
        EnsureOwner(portfolio, actor.UserId);
        var normalized = Normalize(request);
        await directory.EnsureOrganizationUsersAsync(
            portfolio.OrganizationId,
            [normalized.OwnerUserId],
            ct);
        await directory.EnsureProjectsManageableAsync(
            portfolio.OrganizationId,
            normalized.ProjectIds,
            ct);
        await directory.EnsureMilestoneLinksAsync(
            portfolio.OrganizationId,
            normalized.MilestoneLinks,
            ct);

        InitiativeDocument initiative;
        if (string.IsNullOrWhiteSpace(initiativeId))
        {
            if (portfolio.Initiatives.Count >= MaximumInitiatives)
                throw new ValidationException($"A portfolio cannot contain more than {MaximumInitiatives} initiatives.");
            initiative = new InitiativeDocument();
            portfolio.Initiatives.Add(initiative);
        }
        else
        {
            initiative = portfolio.Initiatives.SingleOrDefault(item => item.Id == initiativeId)
                ?? throw new NotFoundException("INITIATIVE_NOT_FOUND", "Initiative was not found.");
        }
        Apply(initiative, normalized);
        ValidateHierarchy(portfolio.Initiatives);
        portfolio.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(portfolio, ct);
        await audit.WriteAsync(
            string.IsNullOrWhiteSpace(initiativeId) ? "InitiativeCreated" : "InitiativeUpdated",
            portfolio.Id,
            null,
            initiative.Name,
            correlationId,
            ct);
        return ToResponse(portfolio, actor.UserId);
    }
}
