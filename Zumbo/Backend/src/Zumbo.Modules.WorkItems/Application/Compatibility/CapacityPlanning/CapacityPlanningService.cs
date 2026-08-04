using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Scenarios;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Snapshots;
using Zumbo.Modules.WorkItems.Application.Policies.CapacityPlanning;
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
    private readonly ArchiveCapacityPlanHandler archiveHandler = new(
        plans,
        audit,
        new CapacityPlanAccessPolicy(directory, currentUser),
        clock,
        expectedVersions);
    private readonly GetCapacityPlanHandler getHandler = new(
        plans,
        new CapacityPlanAccessPolicy(directory, currentUser));
    private readonly ListCapacityPlansHandler listHandler = new(
        plans,
        new CapacityPlanAccessPolicy(directory, currentUser));
    private readonly ShareCapacityPlanHandler shareHandler = new(
        plans,
        directory,
        audit,
        new CapacityPlanAccessPolicy(directory, currentUser),
        clock,
        expectedVersions);
    private readonly SaveCapacityPlanHandler saveHandler = new(
        plans,
        directory,
        audit,
        new CapacityPlanAccessPolicy(directory, currentUser),
        clock,
        expectedVersions);
    private readonly GetCapacitySnapshotHandler snapshotHandler = new(
        plans,
        workItems,
        directory,
        new CapacityPlanAccessPolicy(directory, currentUser),
        clock);
    private readonly PreviewScenarioHandler scenarioHandler = new(
        plans,
        workItems,
        directory,
        new CapacityPlanAccessPolicy(directory, currentUser),
        clock);
}
