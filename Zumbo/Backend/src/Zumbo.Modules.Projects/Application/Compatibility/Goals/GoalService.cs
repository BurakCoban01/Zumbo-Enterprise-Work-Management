using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService(
    IDocumentRepository<GoalDocument> goals,
    IGoalDirectory directory,
    IGoalAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private const int MaximumViewers = 50;
    private const int MaximumInitiativeLinks = 20;
    private const int MaximumProjectLinks = 20;
    private const int MaximumKeyResults = 50;
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);
}
