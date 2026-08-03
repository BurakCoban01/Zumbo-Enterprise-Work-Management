using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    private static string NormalizeSwimlaneMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        null or "" or "none" => "None",
        "assignee" => "Assignee",
        "priority" => "Priority",
        "team" => "Team",
        "epic" => "Epic",
        _ => throw new ValidationException("Swimlane mode must be None, Assignee, Priority, Team or Epic.")
    };
}
