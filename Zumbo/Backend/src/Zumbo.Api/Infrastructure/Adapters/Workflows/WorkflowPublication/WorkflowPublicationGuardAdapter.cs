using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

public sealed class WorkflowPublicationGuardAdapter(
    IDocumentRepository<BoardDocument> boards,
    IDocumentRepository<WorkItemDocument> workItems) : IWorkflowPublicationGuard
{
    public async Task ValidateAsync(WorkflowPublicationCandidate candidate, CancellationToken ct)
    {
        var statusNames = candidate.Statuses.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeBoards = await ReadAllAsync(boards, x => x.ProjectId == candidate.ProjectId && !x.Archived, ct);
        var activeItems = await ReadAllAsync(workItems, x => x.ProjectId == candidate.ProjectId && !x.Archived, ct);

        foreach (var item in activeItems)
        {
            if (!statusNames.Contains(item.Status))
            {
                throw new ConflictException("WORKFLOW_PUBLISH_EXISTING_STATUS_INVALID", "Publish would invalidate an existing work item status.");
            }
            var scheme = candidate.IssueTypeSchemes.SingleOrDefault(x =>
                    x.IssueType.Equals(item.Type, StringComparison.OrdinalIgnoreCase))
                ?? candidate.IssueTypeSchemes.SingleOrDefault(x => x.IssueType == "*")
                ?? throw new ConflictException("WORKFLOW_PUBLISH_ISSUE_SCHEME_MISSING", "Publish would leave an existing issue type without a scheme.");
            if (!scheme.Statuses.Contains(item.Status, StringComparer.OrdinalIgnoreCase))
            {
                throw new ConflictException("WORKFLOW_PUBLISH_EXISTING_SCHEME_INVALID", "Publish would invalidate an existing work item issue scheme.");
            }
        }

        foreach (var board in activeBoards)
        {
            var mappings = board.Columns.SelectMany(column => StatusNames(board, column)
                .Select(status => (Column: column, Status: status))).ToList();
            if (statusNames.Any(status => mappings.Count(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase)) != 1))
            {
                throw new ConflictException("WORKFLOW_PUBLISH_BOARD_MAPPING_INVALID", "Every published status must map to exactly one column on each active board.");
            }

            foreach (var item in activeItems.Where(x => x.BoardId == board.Id))
            {
                var column = board.Columns.SingleOrDefault(x => x.Id == item.ColumnId);
                if (column is null || !StatusNames(board, column).Contains(item.Status, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ConflictException("WORKFLOW_PUBLISH_EXISTING_BOARD_INVALID", "Publish would invalidate an existing work item board placement.");
                }
            }
        }
    }

    private static async Task<IReadOnlyCollection<T>> ReadAllAsync<T>(
        IDocumentRepository<T> repository,
        System.Linq.Expressions.Expression<Func<T, bool>> filter,
        CancellationToken ct) where T : class, IDocument
    {
        var result = new List<T>();
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return result;
    }

    private static IReadOnlyCollection<string> StatusNames(BoardDocument board, BoardColumnDocument column) =>
        board.WorkflowMappingVersion > 0 ? column.StatusNames : [column.Name];
}
