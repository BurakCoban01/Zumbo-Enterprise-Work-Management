using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;

namespace Zumbo.RepositoryContracts;

public abstract class GoalRepositoryContract
{
    protected abstract IDocumentRepository<GoalDocument> Goals();

    [Fact]
    public async Task StorePreservesLinksProgressHistoryAndCompareExchange()
    {
        var repository = Goals();
        var prefix = "feature005-contract-" + Guid.NewGuid().ToString("N");
        var goal = new GoalDocument
        {
            Id = prefix + "-goal",
            OrganizationId = prefix + "-organization",
            OwnerUserId = prefix + "-owner",
            Name = "Provider goal",
            PeriodStartAtUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            PeriodEndAtUtc = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
            Status = GoalStatuses.Active,
            Health = GoalHealth.OnTrack,
            Confidence = 75,
            ViewerUserIds = [prefix + "-viewer"],
            InitiativeLinks =
            [
                new GoalInitiativeLinkDocument
                {
                    PortfolioId = prefix + "-portfolio",
                    InitiativeId = prefix + "-initiative"
                }
            ],
            ProjectIds = [prefix + "-project"],
            KeyResults =
            [
                new KeyResultDocument
                {
                    Id = prefix + "-key-result",
                    OwnerUserId = prefix + "-owner",
                    Name = "Provider key result",
                    BaselineValue = 0,
                    TargetValue = 100,
                    CurrentValue = 40,
                    Unit = "%",
                    Direction = KeyResultDirections.Increase,
                    Confidence = 70,
                    ProgressUpdates =
                    [
                        new KeyResultProgressUpdateDocument
                        {
                            Id = prefix + "-progress",
                            PreviousValue = 20,
                            CurrentValue = 40,
                            Confidence = 70,
                            Note = "Provider progress history",
                            AuthorUserId = prefix + "-owner",
                            CreatedAt = DateTimeOffset.UtcNow
                        }
                    ]
                }
            ],
            StatusUpdates =
            [
                new GoalStatusUpdateDocument
                {
                    Id = prefix + "-status",
                    Status = GoalStatuses.Active,
                    Health = GoalHealth.OnTrack,
                    Confidence = 75,
                    Note = "Provider status history",
                    AuthorUserId = prefix + "-owner",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            goal = await repository.CreateAsync(goal);
            var stale = await repository.SelectAsync(item => item.Id == goal.Id);
            var loaded = await repository.SelectAsync(item =>
                item.Id == goal.Id
                && item.OrganizationId == goal.OrganizationId
                && item.OwnerUserId == goal.OwnerUserId);
            Assert.NotNull(loaded);
            Assert.Equal(prefix + "-viewer", Assert.Single(loaded.ViewerUserIds));
            Assert.Equal(prefix + "-initiative",
                Assert.Single(loaded.InitiativeLinks).InitiativeId);
            Assert.Equal(40, Assert.Single(loaded.KeyResults).CurrentValue);
            Assert.Equal("Provider progress history",
                Assert.Single(Assert.Single(loaded.KeyResults).ProgressUpdates).Note);
            Assert.Equal("Provider status history", Assert.Single(loaded.StatusUpdates).Note);

            goal.Name = "Updated provider goal";
            goal.UpdatedAt = goal.UpdatedAt.AddMinutes(1);
            var replaced = await repository.ReplaceByVersionAsync(
                item => item.Id == goal.Id
                    && item.OrganizationId == goal.OrganizationId,
                goal,
                goal.Version);
            Assert.True(replaced.Found);
            goal.Version = replaced.Version!.Value;

            stale!.Archived = true;
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                repository.ReplaceByVersionAsync(
                    item => item.Id == stale.Id,
                    stale,
                    stale.Version));
            Assert.Null(await repository.SelectAsync(item =>
                item.Id == goal.Id
                && item.OrganizationId == prefix + "-foreign"));
        }
        finally
        {
            await repository.DeleteByFilterAsync(item => item.Id == goal.Id);
        }
    }
}
