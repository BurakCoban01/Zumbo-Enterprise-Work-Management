using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryPortfolioRepositoryContractTests : PortfolioRepositoryContract
{
    protected override ApplicationPersistence.IDocumentRepository<PortfolioDocument> Portfolios() =>
        new InMemoryDocumentRepository<PortfolioDocument>();
}
