using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryGoalRepositoryContractTests : GoalRepositoryContract
{
    protected override ApplicationPersistence.IDocumentRepository<GoalDocument> Goals() =>
        new InMemoryDocumentRepository<GoalDocument>();
}
