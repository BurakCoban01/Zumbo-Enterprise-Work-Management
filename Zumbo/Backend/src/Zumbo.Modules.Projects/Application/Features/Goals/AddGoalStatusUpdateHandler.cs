using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed class AddGoalStatusUpdateHandler(GoalService service)
{
    private AddGoalStatusUpdateSlice? slice;

    public AddGoalStatusUpdateHandler(
        IDocumentRepository<GoalDocument> goals,
        IGoalAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(goals, audit, currentUser, clock, new ExpectedVersionState(expectedVersions))
    {
    }

    internal AddGoalStatusUpdateHandler(
        IDocumentRepository<GoalDocument> goals,
        IGoalAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        ExpectedVersionState expectedVersion)
        : this(null!) =>
        slice = new AddGoalStatusUpdateSlice(
            new GoalReadAccess(goals, currentUser),
            new GoalMutationPersistence(goals, expectedVersion),
            audit,
            clock);

    public Task<GoalResponse> HandleAsync(
        AddGoalStatusUpdateCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.AddStatusUpdateAsync(
            command.GoalId,
            command.Request,
            command.CorrelationId,
            ct);
}
