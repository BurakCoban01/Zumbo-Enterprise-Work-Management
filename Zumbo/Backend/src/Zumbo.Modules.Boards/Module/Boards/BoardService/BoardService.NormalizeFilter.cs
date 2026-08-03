using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

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
}
