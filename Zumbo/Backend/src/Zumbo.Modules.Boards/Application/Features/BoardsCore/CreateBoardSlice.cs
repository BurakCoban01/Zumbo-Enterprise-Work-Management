using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

internal sealed class CreateBoardSlice(
    IDocumentRepository<BoardDocument> boards,
    IBoardProjectAccessChecker accessChecker,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser,
    IBoardAuditWriter audit)
{
    internal async Task<BoardResponse> HandleAsync(
        CreateBoardRequest request,
        string correlationId,
        CancellationToken ct)
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
        return BoardResponseMapper.ToResponse(board, currentUser);
    }

    private async Task EnsurePermissionAsync(string projectId, string permission, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        await accessChecker.EnsureCanAsync(userId, projectId, permission, ct);
    }

    private async Task<IAsyncDisposable> AcquireLockAsync(string resource, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        return await distributedLockProvider.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("BOARD_RESOURCE_BUSY", "Board resource is busy; retry the operation.");
    }

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 100)
        {
            throw new ValidationException("Board name must contain 2-100 characters.");
        }

        return normalized;
    }

    private static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type) || string.Equals(type, "Kanban", StringComparison.OrdinalIgnoreCase))
        {
            return "Kanban";
        }

        if (string.Equals(type, "Scrum", StringComparison.OrdinalIgnoreCase))
        {
            return "Scrum";
        }

        throw new ValidationException("Board type must be Kanban or Scrum.");
    }
}
