using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryCapacityPlanRepositoryContractTests
    : CapacityPlanRepositoryContract
{
    protected override ApplicationPersistence.IDocumentRepository<CapacityPlanDocument> Plans() =>
        new InMemoryDocumentRepository<CapacityPlanDocument>();
}
