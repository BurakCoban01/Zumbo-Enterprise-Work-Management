using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

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
