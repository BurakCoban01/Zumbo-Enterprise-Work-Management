using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.RepositoryContracts;

namespace Zumbo.UnitTests;

public sealed class InMemoryIdentityCredentialStoreContractTests : IdentityCredentialStoreContract
{
    private readonly IDocumentRepository<RefreshSessionDocument> sessions =
        new Zumbo.BuildingBlocks.Infrastructure.Persistence.InMemoryDocumentRepository<RefreshSessionDocument>();
    private readonly IDocumentRepository<ApiKeyDocument> apiKeys =
        new Zumbo.BuildingBlocks.Infrastructure.Persistence.InMemoryDocumentRepository<ApiKeyDocument>();

    protected override IDocumentRepository<RefreshSessionDocument> CreateSessionRepository() => sessions;

    protected override IDocumentRepository<ApiKeyDocument> CreateApiKeyRepository() => apiKeys;
}
