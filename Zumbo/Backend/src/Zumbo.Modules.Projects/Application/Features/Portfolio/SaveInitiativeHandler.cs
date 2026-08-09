using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed class SaveInitiativeHandler(PortfolioService service)
{
    private SaveInitiativeSlice? slice;

    public SaveInitiativeHandler(
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

    internal SaveInitiativeHandler(
        IDocumentRepository<PortfolioDocument> portfolios,
        IPortfolioDirectory directory,
        IPortfolioAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        ExpectedVersionState expectedVersion)
        : this(null!) =>
        slice = new SaveInitiativeSlice(
            new PortfolioReadAccess(portfolios, currentUser),
            new PortfolioMutationPersistence(portfolios, expectedVersion),
            directory,
            audit,
            clock);

    public Task<PortfolioResponse> HandleAsync(
        SaveInitiativeCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.SaveInitiativeAsync(
            command.PortfolioId,
            command.InitiativeId,
            command.Request,
            command.CorrelationId,
            ct);
}
