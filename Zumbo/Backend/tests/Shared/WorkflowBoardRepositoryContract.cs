using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Boards;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

namespace Zumbo.RepositoryContracts;

public abstract class WorkflowBoardRepositoryContract
{
    protected abstract Task<WorkflowBoardRepositoryFixture> CreateFixtureAsync();

    [Fact]
    public async Task VersionMappingAndWipProjection_AreProviderNeutral()
    {
        await using var fixture = await CreateFixtureAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var projectId = "workflow-contract-" + suffix;
        var boardId = "board-contract-" + suffix;
        var columnId = "column-contract-" + suffix;
        var workflow = await fixture.Workflows.CreateAsync(new WorkflowDefinitionDocument
        {
            ProjectId = projectId,
            PublishedVersion = 1,
            Statuses =
            [
                new WorkflowStatusDocument { Name = "Open", Category = "Todo" },
                new WorkflowStatusDocument { Name = "Done", Category = "Done" }
            ],
            Transitions = [new WorkflowTransitionDocument { FromStatus = "Open", ToStatus = "Done" }],
            IssueTypeSchemes =
            [
                new WorkflowIssueTypeSchemeDocument
                {
                    IssueType = "Task",
                    DefaultStatus = "Open",
                    Statuses = ["Open", "Done"],
                    DoneStatuses = ["Done"]
                }
            ],
            PublishedVersions =
            [
                new WorkflowVersionDocument
                {
                    Number = 1,
                    State = "Published",
                    CreatedAt = fixture.Now,
                    PublishedAt = fixture.Now
                }
            ],
            CreatedAt = fixture.Now,
            UpdatedAt = fixture.Now
        });
        var board = await fixture.Boards.CreateAsync(new BoardDocument
        {
            Id = boardId,
            ProjectId = projectId,
            Name = "Contract Board",
            WorkflowMappingVersion = 1,
            Columns =
            [
                new BoardColumnDocument
                {
                    Id = columnId,
                    Name = "Delivery",
                    Category = "InProgress",
                    Position = 1,
                    WipLimit = 2,
                    StatusNames = ["Open", "Done"]
                }
            ],
            CreatedAt = fixture.Now,
            UpdatedAt = fixture.Now
        });
        var wip = new WorkItemWipProjection(fixture.WorkItems, fixture.Projections, fixture.Clock);
        await wip.ReserveCreateAsync(projectId, boardId, new BoardPlacement(columnId, "Open", true, 2), CancellationToken.None);

        var persistedWorkflow = await fixture.Workflows.SelectAsync(x => x.Id == workflow.Id);
        var persistedBoard = await fixture.Boards.SelectAsync(x => x.Id == board.Id);
        var firstWriter = await fixture.Projections.SelectAsync(x => x.Id == $"{boardId}:{columnId}");
        var staleWriter = await fixture.Projections.SelectAsync(x => x.Id == $"{boardId}:{columnId}");
        Assert.NotNull(persistedWorkflow);
        Assert.NotNull(persistedBoard);
        Assert.NotNull(firstWriter);
        Assert.NotNull(staleWriter);
        Assert.Equal(1, persistedWorkflow!.PublishedVersion);
        Assert.Single(persistedWorkflow.IssueTypeSchemes);
        Assert.Equal(["Open", "Done"], persistedBoard!.Columns.Single().StatusNames);
        Assert.Equal(1, firstWriter!.ActiveCount);

        firstWriter.ActiveCount = 0;
        var changed = await fixture.Projections.ReplaceByVersionAsync(
            x => x.Id == firstWriter.Id,
            firstWriter,
            firstWriter.Version);
        staleWriter!.ActiveCount = 2;
        var conflict = await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
            fixture.Projections.ReplaceByVersionAsync(
                x => x.Id == staleWriter.Id,
                staleWriter,
                staleWriter.Version));
        Assert.Equal(changed.Version, conflict.ActualVersion);
    }

    [Fact]
    public async Task RankRebalance_IsDeterministicBoundedAndIdempotent()
    {
        await using var fixture = await CreateFixtureAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var boardId = "rank-board-" + suffix;
        var columnId = "rank-column-" + suffix;
        foreach (var (id, rank) in new[]
                 {
                     ("c-" + suffix, 10L),
                     ("a-" + suffix, 10L),
                     ("b-" + suffix, 11L),
                     ("d-" + suffix, 12L),
                     ("e-" + suffix, 13L)
                 })
        {
            await fixture.WorkItems.CreateAsync(new WorkItemDocument
            {
                Id = id,
                ProjectId = "rank-project-" + suffix,
                BoardId = boardId,
                ColumnId = columnId,
                Status = "To Do",
                Title = id,
                Rank = rank,
                CreatedAt = fixture.Now,
                UpdatedAt = fixture.Now
            });
        }

        var ranks = new WorkItemRankService(
            fixture.WorkItems,
            fixture.Clock,
            Options.Create(new WorkItemRankOptions { BatchSize = 2, MaxBatchesPerRun = 10 }));
        var first = await ranks.RebalanceAsync(boardId, columnId, null, CancellationToken.None);
        var afterFirst = await fixture.WorkItems.ListByFilterAsync(
            item => item.BoardId == boardId && item.ColumnId == columnId,
            item => item.Rank,
            pageSize: 20);
        var versions = afterFirst.ToDictionary(item => item.Id, item => item.Version, StringComparer.Ordinal);

        Assert.Equal(5, first.Changed);
        Assert.InRange(first.Batches, 1, 10);
        Assert.Equal(
            ["a-" + suffix, "c-" + suffix, "b-" + suffix, "d-" + suffix, "e-" + suffix],
            afterFirst.Select(item => item.Id));
        Assert.Equal(
            [1_000_000L, 2_000_000L, 3_000_000L, 4_000_000L, 5_000_000L],
            afterFirst.Select(item => item.Rank));
        Assert.All(afterFirst, item => Assert.Null(item.RankRebalanceToken));

        var second = await ranks.RebalanceAsync(boardId, columnId, null, CancellationToken.None);
        var afterSecond = await fixture.WorkItems.ListByFilterAsync(
            item => item.BoardId == boardId && item.ColumnId == columnId,
            item => item.Rank,
            pageSize: 20);

        Assert.Equal(0, second.Changed);
        Assert.Equal(versions, afterSecond.ToDictionary(item => item.Id, item => item.Version, StringComparer.Ordinal));
        var moving = afterSecond.Single(item => item.Id == "e-" + suffix);
        var anchor = afterSecond.Single(item => item.Id == "b-" + suffix);
        var predecessor = afterSecond.Single(item => item.Id == "c-" + suffix);
        predecessor.Rank = 1;
        anchor.Rank = 2;
        await fixture.WorkItems.ReplaceByVersionAsync(
            item => item.Id == predecessor.Id,
            predecessor,
            predecessor.Version);
        await fixture.WorkItems.ReplaceByVersionAsync(
            item => item.Id == anchor.Id,
            anchor,
            anchor.Version);
        var resolvedRank = await ranks.ResolveReorderRankAsync(
            moving,
            new ReorderWorkItemRequest(anchor.Id, null),
            CancellationToken.None);
        Assert.Equal(1_500_000, resolvedRank);
        Assert.Equal(5, await fixture.WorkItems.DeleteByFilterAsync(item => item.BoardId == boardId));
    }

    [Fact]
    public async Task SprintSnapshotsAndVersioning_AreProviderNeutral()
    {
        await using var fixture = await CreateFixtureAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var sprintId = "sprint-contract-" + suffix;
        var workItemId = "sprint-item-" + suffix;
        var sprint = await fixture.Sprints.CreateAsync(new SprintDocument
        {
            Id = sprintId,
            ProjectId = "sprint-project-" + suffix,
            Name = "Sprint Contract",
            StartAtUtc = fixture.Now,
            EndAtUtc = fixture.Now.AddDays(6),
            CreatedAt = fixture.Now,
            UpdatedAt = fixture.Now
        });
        await fixture.ScopeSnapshots.CreateAsync(new SprintScopeSnapshotDocument
        {
            Id = $"{sprintId}:{workItemId}",
            SprintId = sprintId,
            ProjectId = sprint.ProjectId,
            WorkItemId = workItemId,
            Title = "Frozen title",
            EstimatePoints = 5,
            CapturedAt = fixture.Now
        });
        await fixture.CompletionSnapshots.CreateAsync(new SprintCompletionSnapshotDocument
        {
            Id = $"{sprintId}:{workItemId}",
            SprintId = sprintId,
            ProjectId = sprint.ProjectId,
            WorkItemId = workItemId,
            CommittedPoints = 5,
            Completed = true,
            CompletedAt = fixture.Now.AddDays(2),
            CapturedAt = fixture.Now.AddDays(6)
        });

        var firstWriter = await fixture.Sprints.SelectAsync(item => item.Id == sprintId);
        var staleWriter = await fixture.Sprints.SelectAsync(item => item.Id == sprintId);
        Assert.NotNull(firstWriter);
        Assert.NotNull(staleWriter);
        firstWriter!.Status = SprintStatuses.Active;
        var changed = await fixture.Sprints.ReplaceByVersionAsync(
            item => item.Id == sprintId,
            firstWriter,
            firstWriter.Version);
        staleWriter!.Status = SprintStatuses.Completed;
        var conflict = await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
            fixture.Sprints.ReplaceByVersionAsync(
                item => item.Id == sprintId,
                staleWriter,
                staleWriter.Version));
        var scope = await fixture.ScopeSnapshots.SelectAsync(item => item.SprintId == sprintId);
        var completion = await fixture.CompletionSnapshots.SelectAsync(item => item.SprintId == sprintId);

        Assert.Equal(changed.Version, conflict.ActualVersion);
        Assert.Equal("Frozen title", scope!.Title);
        Assert.Equal(5, completion!.CommittedPoints);
        Assert.True(completion.Completed);
        Assert.Equal(1, await fixture.ScopeSnapshots.DeleteByFilterAsync(item => item.SprintId == sprintId));
        Assert.Equal(1, await fixture.CompletionSnapshots.DeleteByFilterAsync(item => item.SprintId == sprintId));
        Assert.Equal(1, await fixture.Sprints.DeleteByFilterAsync(item => item.Id == sprintId));
    }
}

public abstract class WorkflowBoardRepositoryFixture(
    IDocumentRepository<WorkflowDefinitionDocument> workflows,
    IDocumentRepository<BoardDocument> boards,
    IDocumentRepository<WorkItemDocument> workItems,
    IDocumentRepository<BoardColumnWipProjectionDocument> projections,
    IDocumentRepository<SprintDocument> sprints,
    IDocumentRepository<SprintScopeSnapshotDocument> scopeSnapshots,
    IDocumentRepository<SprintCompletionSnapshotDocument> completionSnapshots) : IAsyncDisposable
{
    public IDocumentRepository<WorkflowDefinitionDocument> Workflows { get; } = workflows;
    public IDocumentRepository<BoardDocument> Boards { get; } = boards;
    public IDocumentRepository<WorkItemDocument> WorkItems { get; } = workItems;
    public IDocumentRepository<BoardColumnWipProjectionDocument> Projections { get; } = projections;
    public IDocumentRepository<SprintDocument> Sprints { get; } = sprints;
    public IDocumentRepository<SprintScopeSnapshotDocument> ScopeSnapshots { get; } = scopeSnapshots;
    public IDocumentRepository<SprintCompletionSnapshotDocument> CompletionSnapshots { get; } = completionSnapshots;
    public DateTimeOffset Now { get; } = new(2026, 7, 20, 18, 30, 0, TimeSpan.Zero);
    public IClock Clock => new FixtureClock(Now);
    public abstract ValueTask DisposeAsync();

    private sealed class FixtureClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
