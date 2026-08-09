using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed class SaveKeyResultHandler(GoalService service)
{
    private SaveKeyResultSlice? slice;

    public SaveKeyResultHandler(
        IDocumentRepository<GoalDocument> goals,
        IGoalDirectory directory,
        IGoalAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(goals, directory, audit, currentUser, clock, new ExpectedVersionState(expectedVersions))
    {
    }

    internal SaveKeyResultHandler(
        IDocumentRepository<GoalDocument> goals,
        IGoalDirectory directory,
        IGoalAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        ExpectedVersionState expectedVersion)
        : this(null!) =>
        slice = new SaveKeyResultSlice(
            new GoalReadAccess(goals, currentUser),
            new GoalMutationPersistence(goals, expectedVersion),
            directory,
            audit,
            clock);

    public Task<GoalResponse> HandleAsync(SaveKeyResultCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.SaveKeyResultAsync(
            command.GoalId,
            command.KeyResultId,
            command.Request,
            command.CorrelationId,
            ct);
}
