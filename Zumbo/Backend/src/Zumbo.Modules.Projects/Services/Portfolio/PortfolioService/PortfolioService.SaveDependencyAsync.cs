using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    public async Task<PortfolioResponse> SaveDependencyAsync(
        string portfolioId,
        string? dependencyId,
        SavePortfolioDependencyRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var portfolio = await GetDocumentAsync(portfolioId, includeArchived: false, ct);
        EnsureOwner(portfolio, actor.UserId);
        var source = Required(request.SourceProjectId, "Source project", 128);
        var target = Required(request.TargetProjectId, "Target project", 128);
        if (source == target)
            throw new ValidationException("A project cannot depend on itself.");
        await directory.EnsureProjectsManageableAsync(
            portfolio.OrganizationId,
            [source, target],
            ct);
        if (!portfolio.Initiatives.Any(item => item.ProjectIds.Contains(source))
            || !portfolio.Initiatives.Any(item => item.ProjectIds.Contains(target)))
        {
            throw new ValidationException("Dependency projects must be linked to portfolio initiatives.");
        }

        PortfolioProjectDependencyDocument dependency;
        if (string.IsNullOrWhiteSpace(dependencyId))
        {
            if (portfolio.Dependencies.Count >= MaximumDependencies)
                throw new ValidationException($"A portfolio cannot contain more than {MaximumDependencies} dependencies.");
            dependency = new PortfolioProjectDependencyDocument();
            portfolio.Dependencies.Add(dependency);
        }
        else
        {
            dependency = portfolio.Dependencies.SingleOrDefault(item => item.Id == dependencyId)
                ?? throw new NotFoundException("PORTFOLIO_DEPENDENCY_NOT_FOUND", "Portfolio dependency was not found.");
        }
        dependency.SourceProjectId = source;
        dependency.TargetProjectId = target;
        dependency.Description = Required(request.Description, "Dependency description", 500);
        dependency.Status = Allowed(
            request.Status,
            PortfolioDependencyStatuses.Allowed,
            "Dependency status");
        dependency.RequiredBy = request.RequiredBy;
        ValidateDependencyGraph(portfolio.Dependencies);
        portfolio.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(portfolio, ct);
        await audit.WriteAsync(
            string.IsNullOrWhiteSpace(dependencyId)
                ? "PortfolioDependencyCreated"
                : "PortfolioDependencyUpdated",
            portfolio.Id,
            null,
            dependency.Id,
            correlationId,
            ct);
        return ToResponse(portfolio, actor.UserId);
    }
}
