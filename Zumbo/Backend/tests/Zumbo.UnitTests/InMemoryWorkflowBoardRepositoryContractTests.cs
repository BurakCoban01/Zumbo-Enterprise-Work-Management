using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Boards;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.RepositoryContracts;

namespace Zumbo.UnitTests;

public sealed class InMemoryWorkflowBoardRepositoryContractTests : WorkflowBoardRepositoryContract
{
    protected override Task<WorkflowBoardRepositoryFixture> CreateFixtureAsync() =>
        Task.FromResult<WorkflowBoardRepositoryFixture>(new Fixture());

    private sealed class Fixture()
        : WorkflowBoardRepositoryFixture(
            new InMemoryDocumentRepository<WorkflowDefinitionDocument>(),
            new InMemoryDocumentRepository<BoardDocument>(),
            new InMemoryDocumentRepository<WorkItemDocument>(),
            new InMemoryDocumentRepository<BoardColumnWipProjectionDocument>(),
            new InMemoryDocumentRepository<SprintDocument>(),
            new InMemoryDocumentRepository<SprintScopeSnapshotDocument>(),
            new InMemoryDocumentRepository<SprintCompletionSnapshotDocument>())
    {
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
