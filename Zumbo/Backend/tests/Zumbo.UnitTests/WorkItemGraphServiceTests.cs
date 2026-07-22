using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class WorkItemGraphServiceTests
{
    private readonly InMemoryDocumentRepository<WorkItemRelationEdgeDocument> edges = new();
    private readonly InMemoryDocumentRepository<WorkItemDocument> workItems = new();

    [Fact]
    public async Task DependencyDirection_IsCanonicalAndRejectsCycles()
    {
        await SeedWorkItemsAsync("a", "b", "c");
        var service = CreateService();

        await service.AddRelationAsync("project-1", "a", "b", "Blocks", default);
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AddRelationAsync("project-1", "b", "a", "BlockedBy", default));
        Assert.Equal("WORK_ITEM_DEPENDENCY_EXISTS", duplicate.Code);

        await service.AddRelationAsync("project-1", "b", "c", "Blocks", default);
        var cycle = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AddRelationAsync("project-1", "c", "a", "Blocks", default));
        Assert.Equal("WORK_ITEM_DEPENDENCY_CYCLE", cycle.Code);
    }

    [Fact]
    public async Task ArchivedIntermediateNode_DoesNotCreateAReachabilityPath()
    {
        await SeedWorkItemsAsync("a", "b");
        await workItems.CreateAsync(Item("archived", archived: true));
        await edges.CreateAsync(Edge("a-to-archived", "a", "archived"));
        await edges.CreateAsync(Edge("archived-to-b", "archived", "b"));
        var service = CreateService();

        await service.AddRelationAsync("project-1", "b", "a", "Blocks", default);

        Assert.NotNull(await edges.SelectAsync(edge =>
            edge.DependencyFromWorkItemId == "b" && edge.DependencyToWorkItemId == "a"));
    }

    [Fact]
    public async Task ActiveBlockers_AreBoundedAndCompletedItemsDoNotBlock()
    {
        await SeedWorkItemsAsync("blocker", "blocked");
        var service = CreateService();
        await service.AddRelationAsync("project-1", "blocker", "blocked", "Blocks", default);

        Assert.Equal(["blocker"], await service.ActiveBlockerIdsAsync("project-1", "blocked", default));

        var blocker = (await workItems.SelectAsync(item => item.Id == "blocker"))!;
        blocker.Status = "Done";
        await workItems.ReplaceByVersionAsync(item => item.Id == blocker.Id, blocker, blocker.Version);

        Assert.Empty(await service.ActiveBlockerIdsAsync("project-1", "blocked", default));
    }

    [Fact]
    public async Task TraversalAndHierarchyLimits_FailWithStableConflictCodes()
    {
        await SeedWorkItemsAsync("a", "b", "c", "d", "parent", "child");
        await edges.CreateAsync(Edge("a-b", "a", "b"));
        await edges.CreateAsync(Edge("a-c", "a", "c"));
        var bounded = CreateService(new WorkItemGraphOptions
        {
            MaxTraversalDepth = 8,
            MaxVisitedNodes = 20,
            MaxOutgoingDependenciesPerNode = 1,
            MaxRelationsPerWorkItem = 20,
            MaxChildrenPerWorkItem = 1
        });

        var graphLimit = await Assert.ThrowsAsync<ConflictException>(() =>
            bounded.AddRelationAsync("project-1", "d", "a", "Blocks", default));
        Assert.Equal("WORK_ITEM_GRAPH_LIMIT", graphLimit.Code);

        var child = (await workItems.SelectAsync(item => item.Id == "child"))!;
        child.ParentId = "parent";
        await workItems.ReplaceByVersionAsync(item => item.Id == child.Id, child, child.Version);
        var childLimit = await Assert.ThrowsAsync<ConflictException>(() =>
            bounded.EnsureCanSetParentAsync("project-1", "d", "parent", default));
        Assert.Equal("WORK_ITEM_CHILD_LIMIT", childLimit.Code);
    }

    [Fact]
    public async Task ParentWalk_DetectsCycleWithoutLoadingTheProject()
    {
        await workItems.CreateAsync(Item("parent", parentId: "child"));
        await workItems.CreateAsync(Item("child"));
        var service = CreateService();

        var cycle = await Assert.ThrowsAsync<ConflictException>(() =>
            service.EnsureCanSetParentAsync("project-1", "child", "parent", default));

        Assert.Equal("WORK_ITEM_HIERARCHY_CYCLE", cycle.Code);
    }

    private WorkItemGraphService CreateService(WorkItemGraphOptions? options = null) => new(
        edges,
        workItems,
        Options.Create(options ?? new WorkItemGraphOptions()),
        new FixedClock());

    private async Task SeedWorkItemsAsync(params string[] ids)
    {
        foreach (var id in ids)
        {
            await workItems.CreateAsync(Item(id));
        }
    }

    private static WorkItemDocument Item(
        string id,
        bool archived = false,
        string? parentId = null) => new()
        {
            Id = id,
            ProjectId = "project-1",
            Status = "To Do",
            Archived = archived,
            ParentId = parentId
        };

    private static WorkItemRelationEdgeDocument Edge(
        string id,
        string from,
        string to) => new()
        {
            Id = id,
            ProjectId = "project-1",
            SourceWorkItemId = from,
            TargetWorkItemId = to,
            RelationType = "Blocks",
            DependencyFromWorkItemId = from,
            DependencyToWorkItemId = to,
            CreatedAt = DateTimeOffset.Parse("2026-07-20T00:00:00Z")
        };

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-07-20T00:00:00Z");
    }
}
