using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class UnlinkWorkItemSlice(UnlinkWorkItemPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(UnlinkWorkItemCommand command, CancellationToken ct)
    {
        var initialWorkItem = await pipeline.GetWorkItemAsync(command.Id, ct);
        await using var structureLock = await pipeline.AcquireStructureLockAsync(
            initialWorkItem.ProjectId,
            ct);
        var workItem = await pipeline.GetForUnlinkAsync(command.Id, ct);
        return await pipeline.RemoveAndPersistAsync(
            workItem,
            command.RelatedWorkItemId,
            NormalizeRelationType(command.RelationType),
            command.CorrelationId,
            ct);
    }

    private static string NormalizeRelationType(string? relationType)
    {
        var requested = string.IsNullOrWhiteSpace(relationType) ? "RelatesTo" : relationType.Trim();
        return requested.ToLowerInvariant() switch
        {
            "blocks" => "Blocks",
            "blockedby" or "blocked-by" => "BlockedBy",
            "relatesto" or "relates-to" => "RelatesTo",
            "duplicates" => "Duplicates",
            _ => throw new ValidationException(
                "Relation type must be Blocks, BlockedBy, RelatesTo or Duplicates.")
        };
    }
}
