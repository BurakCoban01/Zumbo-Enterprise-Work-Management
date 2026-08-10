using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

internal sealed class SaveInitiativeSlice(
    PortfolioReadAccess access,
    PortfolioMutationPersistence persistence,
    IPortfolioDirectory directory,
    IPortfolioAuditWriter audit,
    IClock clock)
{
    internal async Task<PortfolioResponse> HandleAsync(
        SaveInitiativeCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var portfolio = await access.GetDocumentAsync(
            command.PortfolioId,
            includeArchived: false,
            ct);
        PortfolioReadAccess.EnsureOwner(portfolio, actor.UserId);
        var normalized = PortfolioValidation.Normalize(command.Request);
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
        if (string.IsNullOrWhiteSpace(command.InitiativeId))
        {
            if (portfolio.Initiatives.Count >= PortfolioValidation.MaximumInitiatives)
            {
                throw new ValidationException(
                    $"A portfolio cannot contain more than {PortfolioValidation.MaximumInitiatives} initiatives.");
            }
            initiative = new InitiativeDocument();
            portfolio.Initiatives.Add(initiative);
        }
        else
        {
            initiative = portfolio.Initiatives.SingleOrDefault(
                item => item.Id == command.InitiativeId)
                ?? throw new NotFoundException(
                    "INITIATIVE_NOT_FOUND",
                    "Initiative was not found.");
        }
        PortfolioMutationMapper.Apply(initiative, normalized);
        PortfolioValidation.ValidateHierarchy(portfolio.Initiatives);
        portfolio.UpdatedAt = clock.UtcNow;
        await persistence.ReplaceAsync(portfolio, ct);
        await audit.WriteAsync(
            string.IsNullOrWhiteSpace(command.InitiativeId)
                ? "InitiativeCreated"
                : "InitiativeUpdated",
            portfolio.Id,
            null,
            initiative.Name,
            command.CorrelationId,
            ct);
        return PortfolioResponseMapper.ToResponse(portfolio, actor.UserId);
    }
}
