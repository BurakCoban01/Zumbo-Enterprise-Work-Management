using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

internal static class BoardResponseMapper
{
    internal static BoardResponse ToResponse(BoardDocument board, ICurrentUser currentUser) =>
        new(
            board.Id,
            board.ProjectId,
            board.Name,
            board.Type,
            board.SwimlaneMode,
            board.Columns.OrderBy(x => x.Position).Select(x =>
                new BoardColumnResponse(
                    x.Id,
                    x.Name,
                    x.Category,
                    x.Position,
                    x.WipLimit,
                    BoardWorkflowMappingRules.EnsureStatusNames(board, x))).ToList(),
            board.Views
                .Where(x => x.IsShared || x.OwnerUserId == CurrentUserId(currentUser))
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

    private static string CurrentUserId(ICurrentUser currentUser) =>
        !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId
            : throw new UnauthorizedException("Authenticated user is required.");
}
