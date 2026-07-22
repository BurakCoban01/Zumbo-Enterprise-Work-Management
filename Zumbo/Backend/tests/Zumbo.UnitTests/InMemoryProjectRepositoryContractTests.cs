using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.RepositoryContracts;

namespace Zumbo.UnitTests;

public sealed class InMemoryProjectRepositoryContractTests : ProjectRepositoryContract
{
    protected override Task<ProjectRepositoryFixture> CreateFixtureAsync() =>
        Task.FromResult<ProjectRepositoryFixture>(new Fixture());

    private sealed class Fixture()
        : ProjectRepositoryFixture(new InMemoryDocumentRepository<ProjectDocument>())
    {
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
