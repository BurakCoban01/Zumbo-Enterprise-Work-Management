using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.RepositoryContracts;

namespace Zumbo.UnitTests;

public sealed class InMemoryAuditRepositoryContractTests : AuditRepositoryContract
{
    protected override Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<AuditLogDocument> Repository() =>
        new InMemoryDocumentRepository<AuditLogDocument>();
}
