using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed class BoardWorkflowMappingService(
    IDocumentRepository<BoardDocument> boards,
    IBoardProjectAccessChecker accessChecker,
    IBoardColumnUsageChecker usageChecker,
    IBoardWorkflowCatalog workflowCatalog,
    ICurrentUser currentUser,
    IClock clock,
    IBoardAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<BoardResponse> ConfigureAsync(
        string boardId,
        ConfigureBoardWorkflowMappingRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var board = await boards.SelectAsync(x => x.Id == boardId && !x.Archived, ct)
            ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        await accessChecker.EnsureCanAsync(userId, board.ProjectId, "BoardManage", ct);
        if (request.Columns is null
            || request.Columns.Count != board.Columns.Count
            || request.Columns.Select(x => x.ColumnId).Distinct(StringComparer.Ordinal).Count() != board.Columns.Count)
        {
            throw new ValidationException("Workflow mapping must include every board column exactly once.");
        }

        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in request.Columns)
        {
            var column = board.Columns.SingleOrDefault(x => x.Id == mapping.ColumnId)
                ?? throw new ValidationException("Workflow mapping contains an unknown board column.");
            column.StatusNames = BoardWorkflowMappingRules.NormalizeStatusNames(mapping.StatusNames, column.Name, allowEmpty: true);
            if (column.StatusNames.Any(status => !mapped.Add(status)))
            {
                throw new ConflictException("BOARD_STATUS_MAPPED_MULTIPLE_TIMES", "A workflow status can map to only one board column.");
            }
        }

        await workflowCatalog.EnsureStatusesAvailableAsync(board.ProjectId, mapped.ToArray(), ct);
        await usageChecker.ValidateMappingAsync(board, ct);
        board.WorkflowMappingVersion = Math.Max(board.WorkflowMappingVersion, 1);
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
        await audit.WriteAsync("BoardWorkflowMappingUpdated", board.Id, null, string.Join(',', mapped), correlationId, ct);
        return BoardWorkflowMappingRules.ToResponse(board, userId);
    }
}
