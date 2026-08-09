using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed class AddKeyResultProgressHandler(GoalService service)
{
    private AddKeyResultProgressSlice? slice;

    public AddKeyResultProgressHandler(
        IDocumentRepository<GoalDocument> goals,
        IGoalAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(goals, audit, currentUser, clock, new ExpectedVersionState(expectedVersions))
    {
    }

    internal AddKeyResultProgressHandler(
        IDocumentRepository<GoalDocument> goals,
        IGoalAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        ExpectedVersionState expectedVersion)
        : this(null!) =>
        slice = new AddKeyResultProgressSlice(
            new GoalReadAccess(goals, currentUser),
            new GoalMutationPersistence(goals, expectedVersion),
            audit,
            clock);

    public Task<GoalResponse> HandleAsync(
        AddKeyResultProgressCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.AddKeyResultProgressAsync(
            command.GoalId,
            command.KeyResultId,
            command.Request,
            command.CorrelationId,
            ct);
}
