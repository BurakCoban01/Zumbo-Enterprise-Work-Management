using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class WorkItemRankingTests
{
    [Fact]
    public async Task Rebalance_IsDeterministicBoundedAndIdempotent()
    {
        var repository = new InMemoryDocumentRepository<WorkItemDocument>();
        await CreateAsync(repository, "c", 10);
        await CreateAsync(repository, "a", 10);
        await CreateAsync(repository, "b", 20);
        await CreateAsync(repository, "d", 21);
        await CreateAsync(repository, "e", 22);
        var service = CreateService(repository, batchSize: 2, maxBatches: 10);

        var first = await service.RebalanceAsync("board-1", "column-1", null, CancellationToken.None);
        var afterFirst = await OrderedAsync(repository);
        var versions = afterFirst.ToDictionary(item => item.Id, item => item.Version, StringComparer.Ordinal);

        Assert.Equal(5, first.ItemCount);
        Assert.Equal(5, first.Changed);
        Assert.InRange(first.Batches, 1, 10);
        Assert.Equal(["a", "c", "b", "d", "e"], afterFirst.Select(item => item.Id));
        Assert.Equal(
            [1_000_000L, 2_000_000L, 3_000_000L, 4_000_000L, 5_000_000L],
            afterFirst.Select(item => item.Rank));
        Assert.All(afterFirst, item => Assert.Null(item.RankRebalanceToken));

        var second = await service.RebalanceAsync("board-1", "column-1", null, CancellationToken.None);
        var afterSecond = await OrderedAsync(repository);

        Assert.Equal(5, second.ItemCount);
        Assert.Equal(0, second.Changed);
        Assert.Equal(versions, afterSecond.ToDictionary(item => item.Id, item => item.Version, StringComparer.Ordinal));
        Assert.Equal(afterFirst.Select(item => item.Rank), afterSecond.Select(item => item.Rank));
    }

    [Fact]
    public async Task Reorder_ExhaustionTriggersRebalanceAndRetriesOnce()
    {
        var repository = new InMemoryDocumentRepository<WorkItemDocument>();
        var first = await CreateAsync(repository, "first", 1);
        var anchor = await CreateAsync(repository, "anchor", 2);
        var moving = await CreateAsync(repository, "moving", 3);
        var service = CreateService(repository, batchSize: 2, maxBatches: 10);

        var rank = await service.ResolveReorderRankAsync(
            moving,
            new ReorderWorkItemRequest(anchor.Id, null),
            CancellationToken.None);

        Assert.Equal(1_500_000, rank);
        Assert.Equal(1_000_000, (await repository.SelectAsync(item => item.Id == first.Id))!.Rank);
        Assert.Equal(2_000_000, (await repository.SelectAsync(item => item.Id == anchor.Id))!.Rank);
        Assert.Equal(3, (await repository.SelectAsync(item => item.Id == moving.Id))!.Rank);
    }

    [Fact]
    public async Task NextRank_LongBoundaryTriggersCanonicalRebalance()
    {
        var repository = new InMemoryDocumentRepository<WorkItemDocument>();
        await CreateAsync(repository, "last", long.MaxValue - 5);
        var service = CreateService(repository, batchSize: 2, maxBatches: 10);

        var rank = await service.NextRankAsync("board-1", "column-1", null, CancellationToken.None);

        Assert.Equal(2_000_000, rank);
        Assert.Equal(1_000_000, (await repository.SelectAsync(item => item.Id == "last"))!.Rank);
    }

    private static WorkItemRankService CreateService(
        InMemoryDocumentRepository<WorkItemDocument> repository,
        int batchSize,
        int maxBatches) =>
        new(
            repository,
            new FixedClock(),
            Options.Create(new WorkItemRankOptions
            {
                BatchSize = batchSize,
                MaxBatchesPerRun = maxBatches
            }));

    private static async Task<WorkItemDocument> CreateAsync(
        InMemoryDocumentRepository<WorkItemDocument> repository,
        string id,
        long rank) =>
        await repository.CreateAsync(new WorkItemDocument
        {
            Id = id,
            ProjectId = "project-1",
            BoardId = "board-1",
            ColumnId = "column-1",
            Status = "To Do",
            Title = id,
            Rank = rank,
            CreatedAt = DateTimeOffset.Parse("2026-07-20T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-07-20T00:00:00Z")
        });

    private static Task<IReadOnlyList<WorkItemDocument>> OrderedAsync(
        InMemoryDocumentRepository<WorkItemDocument> repository) =>
        repository.ListByFilterAsync(
            item => !item.Archived && item.BoardId == "board-1" && item.ColumnId == "column-1",
            item => item.Rank,
            pageSize: 200);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-07-20T12:00:00Z");
    }
}
