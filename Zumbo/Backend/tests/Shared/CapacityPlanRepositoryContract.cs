using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;

namespace Zumbo.RepositoryContracts;

public abstract class CapacityPlanRepositoryContract
{
    protected abstract IDocumentRepository<CapacityPlanDocument> Plans();

    [Fact]
    public async Task StorePreservesPeopleAllocationsSharingAndCompareExchange()
    {
        var repository = Plans();
        var prefix = "feature006-contract-" + Guid.NewGuid().ToString("N");
        var plan = new CapacityPlanDocument
        {
            Id = prefix + "-plan",
            OrganizationId = prefix + "-organization",
            OwnerUserId = prefix + "-owner",
            Name = "Provider capacity plan",
            PeriodStartUtc = DateTimeOffset.UtcNow,
            PeriodEndUtc = DateTimeOffset.UtcNow.AddDays(30),
            ProjectIds = [prefix + "-project"],
            Members =
            [
                new CapacityMemberDocument
                {
                    UserId = prefix + "-member",
                    TeamId = prefix + "-team",
                    WeeklyCapacityHours = 40
                }
            ],
            Allocations =
            [
                new CapacityAllocationDocument
                {
                    Id = prefix + "-allocation",
                    UserId = prefix + "-member",
                    ProjectId = prefix + "-project",
                    StartDateUtc = DateTimeOffset.UtcNow,
                    EndDateUtc = DateTimeOffset.UtcNow.AddDays(14),
                    Percent = 60
                }
            ],
            ViewerUserIds = [prefix + "-viewer"],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            plan = await repository.CreateAsync(plan);
            var stale = await repository.SelectAsync(item => item.Id == plan.Id);
            var loaded = await repository.SelectAsync(item =>
                item.Id == plan.Id
                && item.OrganizationId == plan.OrganizationId
                && item.OwnerUserId == plan.OwnerUserId);
            Assert.NotNull(loaded);
            Assert.Equal(40, Assert.Single(loaded.Members).WeeklyCapacityHours);
            Assert.Equal(60, Assert.Single(loaded.Allocations).Percent);
            Assert.Equal(prefix + "-viewer", Assert.Single(loaded.ViewerUserIds));

            plan.Name = "Updated capacity plan";
            plan.UpdatedAt = plan.UpdatedAt.AddMinutes(1);
            var replaced = await repository.ReplaceByVersionAsync(
                item => item.Id == plan.Id
                    && item.OrganizationId == plan.OrganizationId,
                plan,
                plan.Version);
            Assert.True(replaced.Found);
            plan.Version = replaced.Version!.Value;

            stale!.Archived = true;
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                repository.ReplaceByVersionAsync(
                    item => item.Id == stale.Id,
                    stale,
                    stale.Version));
            Assert.Null(await repository.SelectAsync(item =>
                item.Id == plan.Id
                && item.OrganizationId == prefix + "-foreign"));
        }
        finally
        {
            await repository.DeleteByFilterAsync(item => item.Id == plan.Id);
        }
    }
}
