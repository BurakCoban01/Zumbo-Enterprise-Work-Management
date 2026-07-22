using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlIdentityCredentialStoreContractTests : IdentityCredentialStoreContract
{
    private readonly IDocumentRepository<RefreshSessionDocument> sessions;
    private readonly IDocumentRepository<ApiKeyDocument> apiKeys;

    public PostgreSqlIdentityCredentialStoreContractTests(PostgreSqlFixture fixture)
    {
        sessions = fixture.Api.CreateRepository<RefreshSessionDocument>("identity", "refresh_sessions");
        apiKeys = fixture.Api.CreateRepository<ApiKeyDocument>("identity", "api_keys");
    }

    protected override IDocumentRepository<RefreshSessionDocument> CreateSessionRepository() => sessions;

    protected override IDocumentRepository<ApiKeyDocument> CreateApiKeyRepository() => apiKeys;
}
