using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed class AddInitiativeStatusUpdateHandler(PortfolioService service)
{
    private AddInitiativeStatusUpdateSlice? slice;

    public AddInitiativeStatusUpdateHandler(
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

    internal AddInitiativeStatusUpdateHandler(
        IDocumentRepository<PortfolioDocument> portfolios,
        IPortfolioAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        ExpectedVersionState expectedVersion)
        : this(null!) =>
        slice = new AddInitiativeStatusUpdateSlice(
            new PortfolioReadAccess(portfolios, currentUser),
            new PortfolioMutationPersistence(portfolios, expectedVersion),
            audit,
            clock);

    public Task<PortfolioResponse> HandleAsync(
        AddInitiativeStatusUpdateCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.AddStatusUpdateAsync(
            command.PortfolioId,
            command.InitiativeId,
            command.Request,
            command.CorrelationId,
            ct);
}
