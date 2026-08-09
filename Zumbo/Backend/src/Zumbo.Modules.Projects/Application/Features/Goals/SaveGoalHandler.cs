using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed class SaveGoalHandler(GoalService service)
{
    private SaveGoalSlice? slice;

    public SaveGoalHandler(
        IDocumentRepository<GoalDocument> goals,
        IGoalDirectory directory,
        IGoalAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(goals, directory, audit, currentUser, clock, new ExpectedVersionState(expectedVersions))
    {
    }

    internal SaveGoalHandler(
        IDocumentRepository<GoalDocument> goals,
        IGoalDirectory directory,
        IGoalAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        ExpectedVersionState expectedVersion)
        : this(null!) =>
        slice = new SaveGoalSlice(
            new GoalReadAccess(goals, currentUser),
            new GoalMutationPersistence(goals, expectedVersion),
            goals,
            directory,
            audit,
            clock);

    public Task<GoalResponse> HandleAsync(SaveGoalCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.SaveAsync(command.GoalId, command.Request, command.CorrelationId, ct);
}
