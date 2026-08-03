using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public Task<BoardResponse> CreateAsync(CreateBoardRequest request, CancellationToken ct) =>
        CreateAsync(request, "none", ct);

    public async Task<BoardResponse> CreateAsync(CreateBoardRequest request, string correlationId, CancellationToken ct)
    {
        CreateBoardValidator.Validate(request);

        await EnsurePermissionAsync(request.ProjectId.Trim(), "BoardManage", ct);
        await using var projectBoardsLock = await AcquireLockAsync("project-boards:" + request.ProjectId.Trim(), ct);
        var name = NormalizeName(request.Name);
        var type = NormalizeType(request.Type);
        var duplicate = await boards.SelectAsync(x =>
            x.ProjectId == request.ProjectId.Trim()
            && !x.Archived
            && x.Name.ToLower() == name.ToLower(), ct);
        if (duplicate is not null)
        {
            throw new ConflictException("BOARD_NAME_EXISTS", "Board name must be unique inside the project.");
        }

        var now = clock.UtcNow;
        var board = new BoardDocument
        {
            ProjectId = request.ProjectId.Trim(),
            Name = name,
            Type = type,
            CreatedAt = now,
            UpdatedAt = now,
            WorkflowMappingVersion = 1,
            Columns =
            [
                new BoardColumnDocument { Name = "To Do", Category = "Todo", Position = 1, StatusNames = ["To Do"] },
                new BoardColumnDocument { Name = "In Progress", Category = "InProgress", Position = 2, WipLimit = 5, StatusNames = ["In Progress", "Blocked"] },
                new BoardColumnDocument { Name = "Code Review", Category = "Review", Position = 3, WipLimit = 3, StatusNames = ["Code Review"] },
                new BoardColumnDocument { Name = "Test", Category = "Test", Position = 4, WipLimit = 4, StatusNames = ["Test"] },
                new BoardColumnDocument { Name = "Done", Category = "Done", Position = 5, StatusNames = ["Done"] }
            ]
        };

        await boards.CreateAsync(board, ct);
        await audit.WriteAsync("BoardCreated", board.Id, null, $"{board.Name}:{board.Type}", correlationId, ct);
        return ToResponse(board);
    }
}
