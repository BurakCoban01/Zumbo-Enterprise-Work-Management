using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryDashboardRepositoryContractTests : DashboardRepositoryContract
{
    protected override ApplicationPersistence.IDocumentRepository<DashboardDocument> Dashboards() =>
        new InMemoryDocumentRepository<DashboardDocument>();
}
