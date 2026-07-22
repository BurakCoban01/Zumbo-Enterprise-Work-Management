using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class WorkItemWipProjectionTests
{
    [Fact]
    public async Task Projection_ReservesReleasesAndEnforcesLimit()
    {
        var workItems = new InMemoryDocumentRepository<WorkItemDocument>();
        var projections = new InMemoryDocumentRepository<BoardColumnWipProjectionDocument>();
        var projection = new WorkItemWipProjection(workItems, projections, new FixedClock());
        var placement = new BoardPlacement("doing", "In Progress", true, 1);

        await projection.ReserveCreateAsync("project-1", "board-1", placement, CancellationToken.None);
        var error = await Assert.ThrowsAsync<ConflictException>(() => projection.ReserveCreateAsync(
            "project-1", "board-1", placement, CancellationToken.None));
        Assert.Equal("BOARD_WIP_LIMIT_EXCEEDED", error.Code);

        await projection.ReleaseAsync(new WorkItemDocument
        {
            ProjectId = "project-1",
            BoardId = "board-1",
            ColumnId = "doing"
        }, CancellationToken.None);
        await projection.ReserveCreateAsync("project-1", "board-1", placement, CancellationToken.None);

        var stored = await projections.SelectAsync(x => x.Id == "board-1:doing");
        Assert.NotNull(stored);
        Assert.Equal(1, stored!.ActiveCount);
        Assert.Equal(4, stored.Version);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 20, 18, 0, 0, TimeSpan.Zero);
    }
}
