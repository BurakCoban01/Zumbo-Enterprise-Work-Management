using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService(
    IDocumentRepository<CapacityPlanDocument> plans,
    IDocumentRepository<WorkItemDocument> workItems,
    ICapacityPlanningDirectory directory,
    ICapacityPlanningAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private const int MaximumProjects = 20;
    private const int MaximumMembers = 100;
    private const int MaximumAllocations = 500;
    private const int MaximumViewers = 50;
    private const int MaximumSourceItems = 10_000;
    private const int SourcePageSize = 500;
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);
}
