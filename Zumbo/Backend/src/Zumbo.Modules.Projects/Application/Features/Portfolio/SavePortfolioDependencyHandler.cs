using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed class SavePortfolioDependencyHandler(PortfolioService service)
{
    private SavePortfolioDependencySlice? slice;

    public SavePortfolioDependencyHandler(
        IDocumentRepository<PortfolioDocument> portfolios,
        IPortfolioDirectory directory,
        IPortfolioAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(
            portfolios,
            directory,
            audit,
            currentUser,
            clock,
            new ExpectedVersionState(expectedVersions))
    {
    }

    internal SavePortfolioDependencyHandler(
        IDocumentRepository<PortfolioDocument> portfolios,
        IPortfolioDirectory directory,
        IPortfolioAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        ExpectedVersionState expectedVersion)
        : this(null!) =>
        slice = new SavePortfolioDependencySlice(
            new PortfolioReadAccess(portfolios, currentUser),
            new PortfolioMutationPersistence(portfolios, expectedVersion),
            directory,
            audit,
            clock);

    public Task<PortfolioResponse> HandleAsync(
        SavePortfolioDependencyCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.SaveDependencyAsync(
            command.PortfolioId,
            command.DependencyId,
            command.Request,
            command.CorrelationId,
            ct);
}
