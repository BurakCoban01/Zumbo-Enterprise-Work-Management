using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed class CreateBoardHandler(BoardService service)
{
    private CreateBoardSlice? slice;

    public CreateBoardHandler(
        IDocumentRepository<BoardDocument> boards,
        IBoardProjectAccessChecker accessChecker,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IClock clock,
        ICurrentUser currentUser,
        IBoardAuditWriter audit)
        : this(null!)
    {
        slice = new CreateBoardSlice(
            boards,
            accessChecker,
            distributedLockProvider,
            distributedLockOptions,
            clock,
            currentUser,
            audit);
    }

    public Task<BoardResponse> HandleAsync(
        CreateBoardRequest request,
        string correlationId,
        CancellationToken ct) =>
        slice?.HandleAsync(request, correlationId, ct)
        ?? service.CreateAsync(request, correlationId, ct);
}
