using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

internal sealed class SavePortfolioDependencySlice(
    PortfolioReadAccess access,
    PortfolioMutationPersistence persistence,
    IPortfolioDirectory directory,
    IPortfolioAuditWriter audit,
    IClock clock)
{
    internal async Task<PortfolioResponse> HandleAsync(
        SavePortfolioDependencyCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var portfolio = await access.GetDocumentAsync(
            command.PortfolioId,
            includeArchived: false,
            ct);
        PortfolioReadAccess.EnsureOwner(portfolio, actor.UserId);
        var source = PortfolioValidation.Required(
            command.Request.SourceProjectId,
            "Source project",
            128);
        var target = PortfolioValidation.Required(
            command.Request.TargetProjectId,
            "Target project",
            128);
        if (source == target)
            throw new ValidationException("A project cannot depend on itself.");
        await directory.EnsureProjectsManageableAsync(
            portfolio.OrganizationId,
            [source, target],
            ct);
        if (!portfolio.Initiatives.Any(item => item.ProjectIds.Contains(source))
            || !portfolio.Initiatives.Any(item => item.ProjectIds.Contains(target)))
        {
            throw new ValidationException(
                "Dependency projects must be linked to portfolio initiatives.");
        }

        PortfolioProjectDependencyDocument dependency;
        if (string.IsNullOrWhiteSpace(command.DependencyId))
        {
            if (portfolio.Dependencies.Count >= PortfolioValidation.MaximumDependencies)
            {
                throw new ValidationException(
                    $"A portfolio cannot contain more than {PortfolioValidation.MaximumDependencies} dependencies.");
            }
            dependency = new PortfolioProjectDependencyDocument();
            portfolio.Dependencies.Add(dependency);
        }
        else
        {
            dependency = portfolio.Dependencies.SingleOrDefault(
                item => item.Id == command.DependencyId)
                ?? throw new NotFoundException(
                    "PORTFOLIO_DEPENDENCY_NOT_FOUND",
                    "Portfolio dependency was not found.");
        }
        dependency.SourceProjectId = source;
        dependency.TargetProjectId = target;
        dependency.Description = PortfolioValidation.Required(
            command.Request.Description,
            "Dependency description",
            500);
        dependency.Status = PortfolioValidation.Allowed(
            command.Request.Status,
            PortfolioDependencyStatuses.Allowed,
            "Dependency status");
        dependency.RequiredBy = command.Request.RequiredBy;
        PortfolioValidation.ValidateDependencyGraph(portfolio.Dependencies);
        portfolio.UpdatedAt = clock.UtcNow;
        await persistence.ReplaceAsync(portfolio, ct);
        await audit.WriteAsync(
            string.IsNullOrWhiteSpace(command.DependencyId)
                ? "PortfolioDependencyCreated"
                : "PortfolioDependencyUpdated",
            portfolio.Id,
            null,
            dependency.Id,
            command.CorrelationId,
            ct);
        return PortfolioResponseMapper.ToResponse(portfolio, actor.UserId);
    }
}
