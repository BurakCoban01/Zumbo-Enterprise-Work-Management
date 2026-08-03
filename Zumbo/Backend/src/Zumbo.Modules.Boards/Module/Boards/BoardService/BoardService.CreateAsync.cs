using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public Task<BoardResponse> CreateAsync(CreateBoardRequest request, CancellationToken ct) =>
        CreateAsync(request, "none", ct);

    public async Task<BoardResponse> CreateAsync(CreateBoardRequest request, string correlationId, CancellationToken ct) =>
        await createBoardHandler.HandleAsync(request, correlationId, ct);
}
