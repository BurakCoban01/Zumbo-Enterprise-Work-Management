using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Swimlanes;

public static class UpdateSwimlaneValidator
{
    public static string NormalizeMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        null or "" or "none" => "None",
        "assignee" => "Assignee",
        "priority" => "Priority",
        "team" => "Team",
        "epic" => "Epic",
        _ => throw new ValidationException("Swimlane mode must be None, Assignee, Priority, Team or Epic.")
    };
}
