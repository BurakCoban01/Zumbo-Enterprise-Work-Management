using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public static class WorkItemBulkOperations
{
    public const string Move = "Move";
    public const string Assign = "Assign";
    public const string Archive = "Archive";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "move" => Move,
        "assign" => Assign,
        "archive" => Archive,
        _ => throw new Zumbo.SharedKernel.ValidationException("Bulk operation must be Move, Assign, or Archive.")
    };
}
