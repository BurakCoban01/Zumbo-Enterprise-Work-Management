using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryPrivacyWorkflowRepositoryContractTests
    : PrivacyWorkflowRepositoryContract
{
    protected override ApplicationPersistence.IDocumentRepository<PrivacyWorkflowDocument> Jobs() =>
        new InMemoryDocumentRepository<PrivacyWorkflowDocument>();
}
