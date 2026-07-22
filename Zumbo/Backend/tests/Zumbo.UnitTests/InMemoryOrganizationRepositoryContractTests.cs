using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Organizations;
using Zumbo.RepositoryContracts;

namespace Zumbo.UnitTests;

public sealed class InMemoryOrganizationRepositoryContractTests : OrganizationRepositoryContract
{
    protected override Task<OrganizationRepositoryFixture> CreateFixtureAsync() =>
        Task.FromResult<OrganizationRepositoryFixture>(new Fixture());

    private sealed class Fixture()
        : OrganizationRepositoryFixture(new InMemoryDocumentRepository<OrganizationDocument>())
    {
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
