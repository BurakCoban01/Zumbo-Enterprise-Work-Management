using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;

namespace Zumbo.RepositoryContracts;

public abstract class DashboardRepositoryContract
{
    protected abstract IDocumentRepository<DashboardDocument> Dashboards();

    [Fact]
    public async Task StorePreservesTenantLayoutFiltersSharingAndCompareExchange()
    {
        var repository = Dashboards();
        var prefix = "feature003-contract-" + Guid.NewGuid().ToString("N");
        var dashboard = new DashboardDocument
        {
            Id = prefix + "-dashboard",
            OrganizationId = prefix + "-organization",
            OwnerUserId = prefix + "-owner",
            Name = "Provider dashboard",
            Scope = DashboardScopes.Portfolio,
            ProjectIds = [prefix + "-project-a", prefix + "-project-b"],
            Filter = new DashboardFilterDocument
            {
                RangeDays = 90,
                DueRiskDays = 14,
                Statuses = ["In Progress"]
            },
            Widgets =
            [
                new DashboardWidgetDocument
                {
                    Id = "summary",
                    Type = DashboardWidgetTypes.ProjectSummary,
                    Title = "Summary",
                    Column = 1,
                    Row = 1,
                    Width = 6,
                    Height = 2
                }
            ],
            ViewerUserIds = [prefix + "-viewer"],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            dashboard = await repository.CreateAsync(dashboard);
            var stale = await repository.SelectAsync(item => item.Id == dashboard.Id);
            var loaded = await repository.SelectAsync(item =>
                item.Id == dashboard.Id
                && item.OrganizationId == dashboard.OrganizationId
                && item.OwnerUserId == dashboard.OwnerUserId);
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded.ProjectIds.Count);
            Assert.Equal(90, loaded.Filter.RangeDays);
            Assert.Equal("summary", Assert.Single(loaded.Widgets).Id);
            Assert.Equal(prefix + "-viewer", Assert.Single(loaded.ViewerUserIds));

            dashboard.Name = "Updated dashboard";
            dashboard.UpdatedAt = dashboard.UpdatedAt.AddMinutes(1);
            var replaced = await repository.ReplaceByVersionAsync(
                item => item.Id == dashboard.Id
                    && item.OrganizationId == dashboard.OrganizationId,
                dashboard,
                dashboard.Version);
            Assert.True(replaced.Found);
            dashboard.Version = replaced.Version!.Value;

            stale!.Archived = true;
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                repository.ReplaceByVersionAsync(
                    item => item.Id == stale.Id,
                    stale,
                    stale.Version));

            Assert.Null(await repository.SelectAsync(item =>
                item.Id == dashboard.Id
                && item.OrganizationId == prefix + "-foreign"));
            var listed = await repository.ListByFilterAsync(
                item => item.OrganizationId == dashboard.OrganizationId
                    && item.OwnerUserId == dashboard.OwnerUserId
                    && !item.Archived,
                item => item.UpdatedAt,
                orderDescending: true,
                pageSize: 20);
            Assert.Equal(dashboard.Id, Assert.Single(listed).Id);
        }
        finally
        {
            await repository.DeleteByFilterAsync(item => item.Id == dashboard.Id);
        }
    }
}
