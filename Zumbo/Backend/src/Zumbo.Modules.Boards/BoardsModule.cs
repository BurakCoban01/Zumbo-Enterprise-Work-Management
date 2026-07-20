using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed record UpdateBoardRequest(string Name, string Type);
public sealed record CreateColumnRequest(
    string Name,
    string Category,
    int? WipLimit,
    IReadOnlyCollection<string>? StatusNames = null);
public sealed record UpdateColumnRequest(
    string Name,
    string Category,
    int? WipLimit,
    IReadOnlyCollection<string>? StatusNames = null);
public sealed record ReorderColumnsRequest(IReadOnlyList<string> ColumnIds);
public sealed record BoardColumnStatusMappingRequest(string ColumnId, IReadOnlyCollection<string> StatusNames);
public sealed record ConfigureBoardWorkflowMappingRequest(IReadOnlyCollection<BoardColumnStatusMappingRequest> Columns);
public sealed record UpdateSwimlaneRequest(string Mode);
public sealed record BoardFilterRequest(
    string? AssigneeUserId,
    string? TeamId,
    IReadOnlyCollection<string>? Statuses,
    IReadOnlyCollection<string>? Priorities,
    IReadOnlyCollection<string>? Labels,
    string? Text);
public sealed record CreateBoardViewRequest(
    string Name,
    bool IsShared,
    string SwimlaneMode,
    BoardFilterRequest Filter);
public sealed record UpdateBoardViewRequest(
    string Name,
    bool IsShared,
    string SwimlaneMode,
    BoardFilterRequest Filter);
public interface IBoardProjectAccessChecker
{
    Task EnsureCanAsync(string userId, string projectId, string permission, CancellationToken ct);
}

public interface IBoardColumnUsageChecker
{
    Task<bool> HasWorkItemsAsync(string boardId, string columnId, string columnName, CancellationToken ct);
    Task<bool> HasBoardWorkItemsAsync(string boardId, CancellationToken ct);
    Task ValidateMappingAsync(BoardDocument board, CancellationToken ct);
}

public interface IBoardWorkflowCatalog
{
    Task EnsureStatusesAvailableAsync(string projectId, IReadOnlyCollection<string> statuses, CancellationToken ct);
}

public interface IBoardAuditWriter
{
    Task WriteAsync(string action, string entityId, string? oldValue, string? newValue, string correlationId, CancellationToken ct);
}

public sealed class BoardService(
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

    public async Task<IReadOnlyList<BoardResponse>> ListByProjectAsync(
        string projectId,
        CancellationToken ct,
        bool archived = false)
    {
        var normalizedProjectId = projectId.Trim();
        await EnsurePermissionAsync(normalizedProjectId, "BoardView", ct);
        var result = await boards.ListByFilterAsync(
            x => x.ProjectId == normalizedProjectId && x.Archived == archived,
            x => x.Name,
            pageSize: 100,
            cancellationToken: ct);

        return result.Select(ToResponse).ToList();
    }

    public Task<BoardResponse> AddColumnAsync(string boardId, CreateColumnRequest request, CancellationToken ct) =>
        AddColumnAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> AddColumnAsync(string boardId, CreateColumnRequest request, string correlationId, CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        var name = NormalizeColumnName(request.Name);
        var category = NormalizeCategory(request.Category);
        ValidateWipLimit(request.WipLimit);
        EnsureUniqueColumn(board, name, category);
        var statusNames = BoardWorkflowMappingRules.NormalizeStatusNames(request.StatusNames, name);
        await BoardWorkflowMappingRules.EnsureAvailableAsync(workflowCatalog, board, statusNames, null, ct);
        var nextPosition = board.Columns.Count == 0 ? 1 : board.Columns.Max(x => x.Position) + 1;
        var column = new BoardColumnDocument
        {
            Name = name,
            Category = category,
            WipLimit = request.WipLimit,
            StatusNames = statusNames,
            Position = nextPosition
        };
        board.Columns.Add(column);

        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardColumnCreated", board.Id, null, $"{column.Id}:{column.Name}:{column.Category}:{column.WipLimit}", correlationId, ct);
        return ToResponse(board);
    }

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

    public async Task<BoardResponse> UpdateColumnAsync(
        string boardId,
        string columnId,
        UpdateColumnRequest request,
        CancellationToken ct)
        => await UpdateColumnAsync(boardId, columnId, request, "none", ct);

    public async Task<BoardResponse> UpdateColumnAsync(
        string boardId,
        string columnId,
        UpdateColumnRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        var column = board.Columns.SingleOrDefault(x => x.Id == columnId)
            ?? throw new NotFoundException("BOARD_COLUMN_NOT_FOUND", "Board column was not found.");
        var name = NormalizeColumnName(request.Name);
        var category = NormalizeCategory(request.Category);
        ValidateWipLimit(request.WipLimit);
        EnsureUniqueColumn(board, name, category, column.Id);
        var statusNames = request.StatusNames is null
            ? BoardWorkflowMappingRules.EnsureStatusNames(board, column)
            : BoardWorkflowMappingRules.NormalizeStatusNames(request.StatusNames, name);
        await BoardWorkflowMappingRules.EnsureAvailableAsync(workflowCatalog, board, statusNames, column.Id, ct);

        var identityChanges = !column.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            || !column.Category.Equals(category, StringComparison.OrdinalIgnoreCase);
        if (identityChanges && column.Category != "Custom")
        {
            throw new ConflictException(
                "BOARD_SYSTEM_COLUMN_LOCKED",
                "Standard workflow column name and category cannot be changed without a workflow migration.");
        }

        if (identityChanges && await usageChecker.HasWorkItemsAsync(board.Id, column.Id, column.Name, ct))
        {
            throw new ConflictException("BOARD_COLUMN_IN_USE", "Move work items before renaming or recategorizing this column.");
        }

        if (column.Category == "Done" && category != "Done")
        {
            throw new ConflictException("DONE_COLUMN_LOCKED", "Done column category cannot be changed without a migration.");
        }

        var oldValue = $"{column.Id}:{column.Name}:{column.Category}:{column.WipLimit}";
        column.Name = name;
        column.Category = category;
        column.WipLimit = request.WipLimit;
        column.StatusNames = statusNames;
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardColumnUpdated", board.Id, oldValue, $"{column.Id}:{column.Name}:{column.Category}:{column.WipLimit}", correlationId, ct);
        return ToResponse(board);
    }

    public Task<BoardResponse> ReorderColumnsAsync(string boardId, ReorderColumnsRequest request, CancellationToken ct) =>
        ReorderColumnsAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> ReorderColumnsAsync(string boardId, ReorderColumnsRequest request, string correlationId, CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        if (request.ColumnIds is null
            || request.ColumnIds.Count != board.Columns.Count
            || request.ColumnIds.Distinct().Count() != request.ColumnIds.Count)
        {
            throw new ValidationException("Column order must include each column exactly once.");
        }

        var oldOrder = string.Join(",", board.Columns.OrderBy(x => x.Position).Select(x => x.Id));
        for (var index = 0; index < request.ColumnIds.Count; index++)
        {
            var column = board.Columns.SingleOrDefault(x => x.Id == request.ColumnIds[index])
                ?? throw new ValidationException("Unknown column id in reorder request.");
            column.Position = index + 1;
        }

        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardColumnsReordered", board.Id, oldOrder, string.Join(",", request.ColumnIds), correlationId, ct);
        return ToResponse(board);
    }

    public Task<BoardResponse> DeleteColumnAsync(string boardId, string columnId, CancellationToken ct) =>
        DeleteColumnAsync(boardId, columnId, "none", ct);

    public async Task<BoardResponse> DeleteColumnAsync(string boardId, string columnId, string correlationId, CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        var column = board.Columns.SingleOrDefault(x => x.Id == columnId)
            ?? throw new NotFoundException("BOARD_COLUMN_NOT_FOUND", "Board column was not found.");

        if (column.Category == "Done")
        {
            throw new ConflictException("DONE_COLUMN_LOCKED", "Done column cannot be removed without a migration.");
        }

        if (column.Category == "Todo")
        {
            throw new ConflictException("TODO_COLUMN_LOCKED", "To Do column cannot be removed without a workflow migration.");
        }

        if (board.Columns.Count <= 1)
        {
            throw new ConflictException("BOARD_REQUIRES_COLUMN", "A board must contain at least one column.");
        }

        if (await usageChecker.HasWorkItemsAsync(board.Id, column.Id, column.Name, ct))
        {
            throw new ConflictException("BOARD_COLUMN_IN_USE", "Move work items before deleting this column.");
        }

        board.Columns.Remove(column);
        var position = 1;
        foreach (var item in board.Columns.OrderBy(x => x.Position))
        {
            item.Position = position++;
        }

        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardColumnDeleted", board.Id, $"{column.Id}:{column.Name}:{column.Category}:{column.WipLimit}", null, correlationId, ct);
        return ToResponse(board);
    }

    public Task ArchiveAsync(string boardId, CancellationToken ct) => ArchiveAsync(boardId, "none", ct);

    public async Task ArchiveAsync(string boardId, string correlationId, CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        if (await usageChecker.HasBoardWorkItemsAsync(board.Id, ct))
        {
            throw new ConflictException("BOARD_IN_USE", "Archive or move active work items before archiving the board.");
        }

        board.Archived = true;
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardArchived", board.Id, "active", "archived", correlationId, ct);
    }

    public Task<BoardResponse> RestoreAsync(string boardId, CancellationToken ct) =>
        RestoreAsync(boardId, "none", ct);

    public async Task<BoardResponse> RestoreAsync(string boardId, string correlationId, CancellationToken ct)
    {
        var board = await GetArchivedBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        var duplicate = await boards.SelectAsync(x =>
            x.Id != board.Id
            && x.ProjectId == board.ProjectId
            && !x.Archived
            && x.Name.ToLower() == board.Name.ToLower(), ct);
        if (duplicate is not null)
        {
            throw new ConflictException("BOARD_NAME_EXISTS", "An active board already uses this name.");
        }

        board.Archived = false;
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardRestored", board.Id, "archived", "active", correlationId, ct);
        return ToResponse(board);
    }

    public async Task<BoardResponse> UpdateSwimlaneAsync(
        string boardId,
        UpdateSwimlaneRequest request,
        CancellationToken ct)
        => await UpdateSwimlaneAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> UpdateSwimlaneAsync(
        string boardId,
        UpdateSwimlaneRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        var oldMode = board.SwimlaneMode;
        board.SwimlaneMode = NormalizeSwimlaneMode(request.Mode);
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardSwimlaneUpdated", board.Id, oldMode, board.SwimlaneMode, correlationId, ct);
        return ToResponse(board);
    }

    public async Task<BoardResponse> CreateViewAsync(
        string boardId,
        CreateBoardViewRequest request,
        CancellationToken ct)
        => await CreateViewAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> CreateViewAsync(
        string boardId,
        CreateBoardViewRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, request.IsShared ? "BoardManage" : "BoardView", ct);
        var userId = CurrentUserId();
        var name = NormalizeViewName(request.Name);
        EnsureUniqueViewName(board, name, userId, request.IsShared);
        var now = clock.UtcNow;
        var view = new BoardViewDocument
        {
            Name = name,
            OwnerUserId = userId,
            IsShared = request.IsShared,
            SwimlaneMode = NormalizeSwimlaneMode(request.SwimlaneMode),
            Filter = NormalizeFilter(request.Filter),
            CreatedAt = now,
            UpdatedAt = now
        };
        board.Views.Add(view);
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardViewCreated", board.Id, null, $"{view.Id}:{view.Name}:{view.IsShared}", correlationId, ct);
        return ToResponse(board);
    }

    public async Task<BoardResponse> UpdateViewAsync(
        string boardId,
        string viewId,
        UpdateBoardViewRequest request,
        CancellationToken ct)
        => await UpdateViewAsync(boardId, viewId, request, "none", ct);

    public async Task<BoardResponse> UpdateViewAsync(
        string boardId,
        string viewId,
        UpdateBoardViewRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        var view = board.Views.SingleOrDefault(x => x.Id == viewId)
            ?? throw new NotFoundException("BOARD_VIEW_NOT_FOUND", "Board view was not found.");
        await EnsureCanMutateViewAsync(board, view, request.IsShared, ct);
        var name = NormalizeViewName(request.Name);
        EnsureUniqueViewName(board, name, view.OwnerUserId, request.IsShared, view.Id);
        var oldValue = $"{view.Id}:{view.Name}:{view.IsShared}:{view.SwimlaneMode}";
        view.Name = name;
        view.IsShared = request.IsShared;
        view.SwimlaneMode = NormalizeSwimlaneMode(request.SwimlaneMode);
        view.Filter = NormalizeFilter(request.Filter);
        view.UpdatedAt = clock.UtcNow;
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardViewUpdated", board.Id, oldValue, $"{view.Id}:{view.Name}:{view.IsShared}:{view.SwimlaneMode}", correlationId, ct);
        return ToResponse(board);
    }

    public async Task<BoardResponse> DeleteViewAsync(
        string boardId,
        string viewId,
        CancellationToken ct)
        => await DeleteViewAsync(boardId, viewId, "none", ct);

    public async Task<BoardResponse> DeleteViewAsync(
        string boardId,
        string viewId,
        string correlationId,
        CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        var view = board.Views.SingleOrDefault(x => x.Id == viewId)
            ?? throw new NotFoundException("BOARD_VIEW_NOT_FOUND", "Board view was not found.");
        await EnsureCanMutateViewAsync(board, view, view.IsShared, ct);
        board.Views.Remove(view);
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardViewDeleted", board.Id, $"{view.Id}:{view.Name}:{view.IsShared}", null, correlationId, ct);
        return ToResponse(board);
    }

    private async Task<BoardDocument> GetBoard(string boardId, CancellationToken ct) =>
        await boards.SelectAsync(x => x.Id == boardId && !x.Archived, ct)
        ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");

    private async Task<BoardDocument> GetArchivedBoard(string boardId, CancellationToken ct) =>
        await boards.SelectAsync(x => x.Id == boardId && x.Archived, ct)
        ?? throw new NotFoundException("BOARD_NOT_FOUND", "Archived board was not found.");

    private async Task EnsurePermissionAsync(string projectId, string permission, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        await accessChecker.EnsureCanAsync(userId, projectId, permission, ct);
    }

    private string CurrentUserId() =>
        !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId
            : throw new UnauthorizedException("Authenticated user is required.");

    private async Task EnsureCanMutateViewAsync(
        BoardDocument board,
        BoardViewDocument view,
        bool targetIsShared,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (!view.IsShared && view.OwnerUserId != userId)
        {
            throw new NotFoundException("BOARD_VIEW_NOT_FOUND", "Board view was not found.");
        }

        await EnsurePermissionAsync(
            board.ProjectId,
            view.IsShared || targetIsShared ? "BoardManage" : "BoardView",
            ct);
    }

    private async Task SaveAsync(BoardDocument board, CancellationToken ct)
    {
        board.UpdatedAt = clock.UtcNow;
        var result = await boards.ReplaceByVersionAsync(
            x => x.Id == board.Id,
            board,
            expectedVersion.Consume(board.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
        }

        board.Version = result.Version!.Value;
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

    private static string NormalizeColumnName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 80)
        {
            throw new ValidationException("Board column name must contain 1-80 characters.");
        }

        return normalized;
    }

    private static string NormalizeSwimlaneMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        null or "" or "none" => "None",
        "assignee" => "Assignee",
        "priority" => "Priority",
        "team" => "Team",
        "epic" => "Epic",
        _ => throw new ValidationException("Swimlane mode must be None, Assignee, Priority, Team or Epic.")
    };

    private static string NormalizeViewName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 80)
        {
            throw new ValidationException("Board view name must contain 2-80 characters.");
        }

        return normalized;
    }

    private static BoardFilterDocument NormalizeFilter(BoardFilterRequest? filter)
    {
        if (filter is null)
        {
            throw new ValidationException("Board view filter is required.");
        }

        var statuses = NormalizeFilterValues(filter.Statuses, "status", 20);
        var priorities = NormalizeFilterValues(filter.Priorities, "priority", 10);
        var labels = NormalizeFilterValues(filter.Labels, "label", 20);
        var text = string.IsNullOrWhiteSpace(filter.Text) ? null : filter.Text.Trim();
        if (text?.Length > 200)
        {
            throw new ValidationException("Board filter text cannot exceed 200 characters.");
        }

        return new BoardFilterDocument
        {
            AssigneeUserId = NormalizeOptionalId(filter.AssigneeUserId),
            TeamId = NormalizeOptionalId(filter.TeamId),
            Statuses = statuses,
            Priorities = priorities,
            Labels = labels,
            Text = text
        };
    }

    private static List<string> NormalizeFilterValues(
        IReadOnlyCollection<string>? values,
        string field,
        int maximumCount)
    {
        var normalized = (values ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count > maximumCount || normalized.Any(x => x.Length > 80))
        {
            throw new ValidationException($"Board filter {field} values exceed the allowed limits.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureUniqueViewName(
        BoardDocument board,
        string name,
        string ownerUserId,
        bool isShared,
        string? ignoredViewId = null)
    {
        if (board.Views.Any(x =>
            x.Id != ignoredViewId
            && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && (isShared || x.IsShared || x.OwnerUserId == ownerUserId)))
        {
            throw new ConflictException("BOARD_VIEW_NAME_EXISTS", "Board view name must be unique in its visibility scope.");
        }
    }

    private static string NormalizeCategory(string category)
    {
        var normalized = string.IsNullOrWhiteSpace(category) ? "Custom" : category.Trim();
        var known = new[] { "Todo", "InProgress", "Review", "Test", "Done", "Custom" };
        return known.SingleOrDefault(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ?? normalized;
    }

    private static void ValidateWipLimit(int? wipLimit)
    {
        if (wipLimit is < 1 or > 1000)
        {
            throw new ValidationException("WIP limit must be between 1 and 1000 when provided.");
        }
    }

    private static void EnsureUniqueColumn(
        BoardDocument board,
        string name,
        string category,
        string? ignoredColumnId = null)
    {
        if (board.Columns.Any(x => x.Id != ignoredColumnId && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("BOARD_COLUMN_NAME_EXISTS", "Column name must be unique inside the board.");
        }

        if (category != "Custom" && board.Columns.Any(x =>
            x.Id != ignoredColumnId && x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("BOARD_COLUMN_CATEGORY_EXISTS", "A board can contain only one standard column per category.");
        }
    }

    private BoardResponse ToResponse(BoardDocument board) =>
        new(
            board.Id,
            board.ProjectId,
            board.Name,
            board.Type,
            board.SwimlaneMode,
            board.Columns.OrderBy(x => x.Position).Select(x =>
                new BoardColumnResponse(x.Id, x.Name, x.Category, x.Position, x.WipLimit, BoardWorkflowMappingRules.EnsureStatusNames(board, x))).ToList(),
            board.Views
                .Where(x => x.IsShared || x.OwnerUserId == CurrentUserId())
                .OrderByDescending(x => x.IsShared)
                .ThenBy(x => x.Name)
                .Select(x => new BoardViewResponse(
                    x.Id,
                    x.Name,
                    x.OwnerUserId,
                    x.IsShared,
                    x.SwimlaneMode,
                    new BoardFilterResponse(
                        x.Filter.AssigneeUserId,
                        x.Filter.TeamId,
                        x.Filter.Statuses,
                        x.Filter.Priorities,
                        x.Filter.Labels,
                        x.Filter.Text)))
                .ToList(),
            board.Archived,
            board.Version);
}
