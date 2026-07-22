using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Teams;
using Zumbo.RepositoryContracts;

namespace Zumbo.UnitTests;

public sealed class InMemoryTeamRepositoryContractTests : TeamRepositoryContract
{
    protected override Task<TeamRepositoryFixture> CreateFixtureAsync() =>
        Task.FromResult<TeamRepositoryFixture>(new Fixture());

    private sealed class Fixture()
        : TeamRepositoryFixture(new InMemoryDocumentRepository<TeamDocument>())
    {
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
