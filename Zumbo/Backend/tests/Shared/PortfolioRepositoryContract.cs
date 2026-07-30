using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;

namespace Zumbo.RepositoryContracts;

public abstract class PortfolioRepositoryContract
{
    protected abstract IDocumentRepository<PortfolioDocument> Portfolios();

    [Fact]
    public async Task StorePreservesHierarchyHistoryDependenciesAndCompareExchange()
    {
        var repository = Portfolios();
        var prefix = "feature004-contract-" + Guid.NewGuid().ToString("N");
        var portfolio = new PortfolioDocument
        {
            Id = prefix + "-portfolio",
            OrganizationId = prefix + "-organization",
            OwnerUserId = prefix + "-owner",
            Name = "Provider portfolio",
            ViewerUserIds = [prefix + "-viewer"],
            Initiatives =
            [
                new InitiativeDocument
                {
                    Id = prefix + "-initiative",
                    Name = "Provider initiative",
                    OwnerUserId = prefix + "-owner",
                    Status = InitiativeStatuses.Active,
                    Health = InitiativeHealth.AtRisk,
                    Confidence = 60,
                    ProjectIds = [prefix + "-project"],
                    StatusUpdates =
                    [
                        new InitiativeStatusUpdateDocument
                        {
                            Id = prefix + "-update",
                            Status = InitiativeStatuses.Active,
                            Health = InitiativeHealth.AtRisk,
                            Confidence = 60,
                            Note = "Provider status history",
                            AuthorUserId = prefix + "-owner",
                            CreatedAt = DateTimeOffset.UtcNow
                        }
                    ]
                }
            ],
            Dependencies =
            [
                new PortfolioProjectDependencyDocument
                {
                    Id = prefix + "-dependency",
                    SourceProjectId = prefix + "-project",
                    TargetProjectId = prefix + "-target",
                    Description = "Provider dependency",
                    Status = PortfolioDependencyStatuses.Active
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            portfolio = await repository.CreateAsync(portfolio);
            var stale = await repository.SelectAsync(item => item.Id == portfolio.Id);
            var loaded = await repository.SelectAsync(item =>
                item.Id == portfolio.Id
                && item.OrganizationId == portfolio.OrganizationId
                && item.OwnerUserId == portfolio.OwnerUserId);
            Assert.NotNull(loaded);
            Assert.Equal(prefix + "-viewer", Assert.Single(loaded.ViewerUserIds));
            Assert.Equal(InitiativeHealth.AtRisk, Assert.Single(loaded.Initiatives).Health);
            Assert.Equal("Provider status history",
                Assert.Single(Assert.Single(loaded.Initiatives).StatusUpdates).Note);
            Assert.Equal(prefix + "-target", Assert.Single(loaded.Dependencies).TargetProjectId);

            portfolio.Name = "Updated portfolio";
            portfolio.UpdatedAt = portfolio.UpdatedAt.AddMinutes(1);
            var replaced = await repository.ReplaceByVersionAsync(
                item => item.Id == portfolio.Id
                    && item.OrganizationId == portfolio.OrganizationId,
                portfolio,
                portfolio.Version);
            Assert.True(replaced.Found);
            portfolio.Version = replaced.Version!.Value;

            stale!.Archived = true;
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                repository.ReplaceByVersionAsync(
                    item => item.Id == stale.Id,
                    stale,
                    stale.Version));
            Assert.Null(await repository.SelectAsync(item =>
                item.Id == portfolio.Id
                && item.OrganizationId == prefix + "-foreign"));
        }
        finally
        {
            await repository.DeleteByFilterAsync(item => item.Id == portfolio.Id);
        }
    }
}
