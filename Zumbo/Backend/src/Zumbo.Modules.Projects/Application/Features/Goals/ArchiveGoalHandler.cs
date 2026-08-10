using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed class ArchiveGoalHandler(GoalService service)
{
    private ArchiveGoalSlice? slice;

    public ArchiveGoalHandler(
        IDocumentRepository<GoalDocument> goals,
        IGoalAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(goals, audit, currentUser, clock, new ExpectedVersionState(expectedVersions))
    {
    }

    internal ArchiveGoalHandler(
        IDocumentRepository<GoalDocument> goals,
        IGoalAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        ExpectedVersionState expectedVersion)
        : this(null!) =>
        slice = new ArchiveGoalSlice(
            new GoalReadAccess(goals, currentUser),
            new GoalMutationPersistence(goals, expectedVersion),
            audit,
            clock);

    public Task HandleAsync(ArchiveGoalCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.ArchiveAsync(command.GoalId, command.CorrelationId, ct);
}
