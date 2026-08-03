using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public Task<BoardResponse> UpdateAsync(string boardId, UpdateBoardRequest request, CancellationToken ct) =>
        UpdateAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> UpdateAsync(string boardId, UpdateBoardRequest request, string correlationId, CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        var name = NormalizeName(request.Name);
        var duplicate = await boards.SelectAsync(x =>
            x.Id != board.Id
            && x.ProjectId == board.ProjectId
            && !x.Archived
            && x.Name.ToLower() == name.ToLower(), ct);
        if (duplicate is not null)
        {
            throw new ConflictException("BOARD_NAME_EXISTS", "Board name must be unique inside the project.");
        }

        var oldValue = $"{board.Name}:{board.Type}";
        board.Name = name;
        board.Type = NormalizeType(request.Type);
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardUpdated", board.Id, oldValue, $"{board.Name}:{board.Type}", correlationId, ct);
        return ToResponse(board);
    }
}
