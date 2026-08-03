using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService(
    IDocumentRepository<BoardDocument> boards,
    IBoardProjectAccessChecker accessChecker,
    IBoardColumnUsageChecker usageChecker,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser,
    IBoardAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null,
    IBoardWorkflowCatalog? workflowCatalog = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);
}
