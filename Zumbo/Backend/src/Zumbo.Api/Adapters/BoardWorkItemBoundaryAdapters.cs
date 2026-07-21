using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

public sealed class BoardProjectAccessCheckerAdapter(
    IProjectResourcePolicy resourcePolicy) : IBoardProjectAccessChecker
{
    public async Task EnsureCanAsync(string userId, string projectId, string permission, CancellationToken ct)
    {
        _ = await resourcePolicy.AuthorizeAsync(projectId, permission, ct);
    }
}

public sealed class WorkItemTeamPolicyAdapter(
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<TeamDocument> teams) : IWorkItemTeamPolicy
{
    public async Task EnsureCanAssignAsync(
        string projectId,
        string teamId,
        string? assigneeUserId,
        CancellationToken ct)
    {
        var project = await projects.SelectAsync(x => x.Id == projectId && !x.Archived, ct)
            ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        if (!project.TeamIds.Contains(teamId))
        {
            throw new ConflictException("WORK_ITEM_TEAM_NOT_LINKED", "Team must be linked to the project.");
        }

        var team = await teams.SelectAsync(x => x.Id == teamId && !x.Archived, ct)
            ?? throw new NotFoundException("TEAM_NOT_FOUND", "Team was not found.");
        if (team.OrganizationId != project.OrganizationId)
        {
            throw new ConflictException("WORK_ITEM_TEAM_ORGANIZATION_MISMATCH", "Team must belong to the project organization.");
        }

        if (!string.IsNullOrWhiteSpace(assigneeUserId)
            && team.Members.All(x => x.UserId != assigneeUserId || x.Status != "Active"))
        {
            throw new ConflictException("WORK_ITEM_ASSIGNEE_NOT_IN_TEAM", "Assignee must be an active member of the work item team.");
        }
    }

    public async Task<IReadOnlyCollection<WorkItemTeamEntry>> ListProjectTeamsAsync(
        string projectId,
        CancellationToken ct)
    {
        var project = await projects.SelectAsync(x => x.Id == projectId && !x.Archived, ct)
            ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        var teamIds = project.TeamIds.ToHashSet(StringComparer.Ordinal);
        var result = await teams.ListByFilterAsync(
            x => teamIds.Contains(x.Id) && !x.Archived,
            x => x.Name,
            pageSize: 100,
            cancellationToken: ct);
        return result.Select(x => new WorkItemTeamEntry(x.Id, x.Name)).ToList();
    }
}

public sealed class BoardPolicyAdapter(
    IDocumentRepository<BoardDocument> boards,
    IDocumentRepository<WorkItemDocument> workItems) : IBoardColumnUsageChecker, IBoardPlacementPolicy
{
    public async Task<BoardPlacement> ResolveInitialAsync(string projectId, string boardId, CancellationToken ct)
    {
        var board = await GetBoardAsync(projectId, boardId, ct);
        var column = board.Columns
            .OrderBy(x => x.Category == "Todo" ? 0 : 1)
            .ThenBy(x => x.Position)
            .FirstOrDefault(x => StatusNames(board, x).Count > 0)
            ?? throw new ConflictException("BOARD_REQUIRES_COLUMN", "Board must contain a column before creating work items.");
        var initialStatus = StatusNames(board, column).First();
        return new BoardPlacement(column.Id, initialStatus, column.WipLimit.HasValue, column.WipLimit);
    }

    public async Task<BoardPlacement> EnsureCanMoveAsync(
        string projectId,
        string boardId,
        string workItemId,
        string targetStatus,
        CancellationToken ct)
    {
        var board = await GetBoardAsync(projectId, boardId, ct);
        var column = board.Columns.SingleOrDefault(x =>
                StatusNames(board, x).Contains(targetStatus, StringComparer.OrdinalIgnoreCase))
            ?? throw new ConflictException("BOARD_STATUS_COLUMN_NOT_FOUND", "Target workflow status has no board column.");
        return new BoardPlacement(column.Id, targetStatus.Trim(), column.WipLimit.HasValue, column.WipLimit);
    }

    public async Task EnsureHasCapacityAsync(
        string boardId,
        string columnId,
        string? ignoredWorkItemId,
        CancellationToken ct)
    {
        var board = await boards.SelectAsync(x => x.Id == boardId && !x.Archived, ct)
            ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
        var column = board.Columns.SingleOrDefault(x => x.Id == columnId)
            ?? throw new NotFoundException("BOARD_COLUMN_NOT_FOUND", "Board column was not found.");
        await EnsureCapacityCoreAsync(board.Id, column, ignoredWorkItemId, ct);
    }

    public async Task<bool> HasWorkItemsAsync(
        string boardId,
        string columnId,
        string columnName,
        CancellationToken ct) =>
        await workItems.ExistsByFilterAsync(x =>
            x.BoardId == boardId
            && !x.Archived
            && (x.ColumnId == columnId || x.ColumnId == "" && x.Status == columnName), ct);

    public async Task<bool> HasBoardWorkItemsAsync(string boardId, CancellationToken ct) =>
        await workItems.ExistsByFilterAsync(x => x.BoardId == boardId && !x.Archived, ct);

    public async Task ValidateMappingAsync(BoardDocument board, CancellationToken ct)
    {
        string? cursor = null;
        do
        {
            var page = await workItems.ListByCursorAsync(
                x => x.BoardId == board.Id && !x.Archived,
                cursor,
                pageSize: 200,
                cancellationToken: ct);
            foreach (var item in page.Items)
            {
                var column = board.Columns.SingleOrDefault(x => x.Id == item.ColumnId)
                    ?? throw new ConflictException("BOARD_MAPPING_EXISTING_ITEM_INVALID", "An existing work item references an unmapped board column.");
                if (!StatusNames(board, column).Contains(item.Status, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ConflictException("BOARD_MAPPING_EXISTING_ITEM_INVALID", "Board mapping cannot invalidate an existing work item status.");
                }
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);
    }

    private async Task<BoardDocument> GetBoardAsync(string projectId, string boardId, CancellationToken ct)
    {
        var board = await boards.SelectAsync(x => x.Id == boardId && !x.Archived, ct)
            ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
        if (board.ProjectId != projectId)
        {
            throw new ConflictException("BOARD_PROJECT_MISMATCH", "Board does not belong to the requested project.");
        }

        return board;
    }

    private async Task EnsureCapacityCoreAsync(
        string boardId,
        BoardColumnDocument column,
        string? ignoredWorkItemId,
        CancellationToken ct)
    {
        if (column.WipLimit is null)
        {
            return;
        }

        var count = await workItems.CountByFilterAsync(
            x => x.BoardId == boardId
                && x.Id != ignoredWorkItemId
                && !x.Archived
                && (x.ColumnId == column.Id || x.ColumnId == "" && x.Status == column.Name),
            ct);

        if (count >= column.WipLimit.Value)
        {
            throw new ConflictException(
                "BOARD_WIP_LIMIT_EXCEEDED",
                $"Column '{column.Name}' has reached its WIP limit of {column.WipLimit.Value}.");
        }
    }

    private static IReadOnlyCollection<string> StatusNames(BoardDocument board, BoardColumnDocument column) =>
        board.WorkflowMappingVersion > 0 ? column.StatusNames : [column.Name];
}

public sealed class BoardWorkflowCatalogAdapter(
    IDocumentRepository<WorkflowDefinitionDocument> workflows) : IBoardWorkflowCatalog
{
    public async Task EnsureStatusesAvailableAsync(
        string projectId,
        IReadOnlyCollection<string> statuses,
        CancellationToken ct)
    {
        var workflow = await workflows.SelectAsync(x => x.ProjectId == projectId, ct);
        if (workflow is null)
        {
            return;
        }

        var available = workflow.Statuses.Select(x => x.Name)
            .Concat(workflow.Draft?.Statuses.Select(x => x.Name) ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (statuses.Any(x => !available.Contains(x)))
        {
            throw new ConflictException("BOARD_WORKFLOW_STATUS_UNKNOWN", "Board mapping contains a status outside the published workflow or current draft.");
        }
    }
}
