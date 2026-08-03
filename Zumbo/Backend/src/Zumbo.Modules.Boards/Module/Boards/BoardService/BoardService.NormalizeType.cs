using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

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
