using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryIntakeRepositoryContractTests : IntakeRepositoryContract
{
    protected override ApplicationPersistence.IDocumentRepository<IntakeFormDocument> Forms() =>
        new InMemoryDocumentRepository<IntakeFormDocument>();

    protected override ApplicationPersistence.IDocumentRepository<IntakeFormVersionDocument> Versions() =>
        new InMemoryDocumentRepository<IntakeFormVersionDocument>();

    protected override ApplicationPersistence.IDocumentRepository<IntakeSubmissionDocument> Submissions() =>
        new InMemoryDocumentRepository<IntakeSubmissionDocument>();
}
