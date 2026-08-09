using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed class ArchivePortfolioHandler(PortfolioService service)
{
    private ArchivePortfolioSlice? slice;

    public ArchivePortfolioHandler(
        IDocumentRepository<PortfolioDocument> portfolios,
        IPortfolioAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(
            portfolios,
            audit,
            currentUser,
            clock,
            new ExpectedVersionState(expectedVersions))
    {
    }

    internal ArchivePortfolioHandler(
        IDocumentRepository<PortfolioDocument> portfolios,
        IPortfolioAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        ExpectedVersionState expectedVersion)
        : this(null!) =>
        slice = new ArchivePortfolioSlice(
            new PortfolioReadAccess(portfolios, currentUser),
            new PortfolioMutationPersistence(portfolios, expectedVersion),
            audit,
            clock);

    public Task HandleAsync(ArchivePortfolioCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.ArchiveAsync(command.PortfolioId, command.CorrelationId, ct);
}
