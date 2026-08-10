using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed class SavePortfolioHandler(PortfolioService service)
{
    private SavePortfolioSlice? slice;

    public SavePortfolioHandler(
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

    internal SavePortfolioHandler(
        IDocumentRepository<PortfolioDocument> portfolios,
        IPortfolioDirectory directory,
        IPortfolioAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        ExpectedVersionState expectedVersion)
        : this(null!) =>
        slice = new SavePortfolioSlice(
            new PortfolioReadAccess(portfolios, currentUser),
            new PortfolioMutationPersistence(portfolios, expectedVersion),
            portfolios,
            directory,
            audit,
            clock);

    public Task<PortfolioResponse> HandleAsync(
        SavePortfolioCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.SaveAsync(
            command.PortfolioId,
            command.Request,
            command.CorrelationId,
            ct);
}
