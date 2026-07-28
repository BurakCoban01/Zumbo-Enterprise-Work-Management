using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Workflows;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryAutomationRepositoryContractTests : AutomationRepositoryContract
{
    protected override ApplicationPersistence.IDocumentRepository<AutomationRuleDocument> Rules() =>
        new InMemoryDocumentRepository<AutomationRuleDocument>();
}
