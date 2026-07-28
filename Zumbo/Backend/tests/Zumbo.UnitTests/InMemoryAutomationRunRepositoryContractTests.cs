using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Workflows;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryAutomationRunRepositoryContractTests
    : AutomationRunRepositoryContract
{
    protected override ApplicationPersistence.IDocumentRepository<AutomationRunDocument> Runs() =>
        new InMemoryDocumentRepository<AutomationRunDocument>();
}
