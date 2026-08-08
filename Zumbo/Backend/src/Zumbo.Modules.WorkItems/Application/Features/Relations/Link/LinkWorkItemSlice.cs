using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class LinkWorkItemSlice(LinkWorkItemPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(LinkWorkItemCommand command, CancellationToken ct)
    {
        var initialWorkItem = await pipeline.GetWorkItemAsync(command.Id, ct);
        await using var structureLock = await pipeline.AcquireStructureLockAsync(
            initialWorkItem.ProjectId,
            ct);
        var workItem = await pipeline.GetForLinkAsync(command.Id, ct);
        var relationType = NormalizeRelationType(command.Request.RelationType);

        if (string.Equals(workItem.Id, command.Request.RelatedWorkItemId, StringComparison.Ordinal))
        {
            throw new ValidationException("A work item cannot be linked to itself.");
        }

        var relatedWorkItem = await pipeline.GetWorkItemAsync(command.Request.RelatedWorkItemId, ct);
        if (!string.Equals(workItem.ProjectId, relatedWorkItem.ProjectId, StringComparison.Ordinal))
        {
            throw new ValidationException("Linked work items must belong to the same project.");
        }

        if (workItem.Relations.Any(item =>
            item.RelatedWorkItemId == relatedWorkItem.Id
            && item.RelationType.Equals(relationType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException(
                "WORK_ITEM_RELATION_EXISTS",
                "Work item relation already exists.");
        }

        await pipeline.AddGraphRelationAsync(
            workItem.ProjectId,
            workItem.Id,
            relatedWorkItem.Id,
            relationType,
            ct);
        return await pipeline.PersistAsync(
            workItem,
            relatedWorkItem.Id,
            relationType,
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
