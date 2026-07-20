using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemGraphOptions
{
    public int MaxTraversalDepth { get; init; } = 64;
    public int MaxVisitedNodes { get; init; } = 1_000;
    public int MaxOutgoingDependenciesPerNode { get; init; } = 200;
    public int MaxRelationsPerWorkItem { get; init; } = 200;
    public int MaxChildrenPerWorkItem { get; init; } = 200;
}

public sealed class WorkItemRelationEdgeDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string SourceWorkItemId { get; set; } = string.Empty;
    public string TargetWorkItemId { get; set; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
    public string? DependencyFromWorkItemId { get; set; }
    public string? DependencyToWorkItemId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class WorkItemGraphService(
    IDocumentRepository<WorkItemRelationEdgeDocument> edges,
    IDocumentRepository<WorkItemDocument> workItems,
    IOptions<WorkItemGraphOptions> configuredOptions,
    IClock clock)
{
    private WorkItemGraphOptions Options => configuredOptions.Value;

    public async Task AddRelationAsync(
        string projectId,
        string sourceWorkItemId,
        string targetWorkItemId,
        string relationType,
        CancellationToken ct)
    {
        var relationCount = await edges.CountByFilterAsync(
            edge => edge.ProjectId == projectId && edge.SourceWorkItemId == sourceWorkItemId,
            ct);
        if (relationCount >= Options.MaxRelationsPerWorkItem)
        {
            throw new ConflictException(
                "WORK_ITEM_RELATION_LIMIT",
                "The work item relation limit has been reached.");
        }

        var dependency = DependencyDirection(sourceWorkItemId, targetWorkItemId, relationType);
        if (dependency is not null)
        {
            if (await edges.ExistsByFilterAsync(
                    edge => edge.ProjectId == projectId
                        && edge.DependencyFromWorkItemId == dependency.Value.From
                        && edge.DependencyToWorkItemId == dependency.Value.To,
                    ct))
            {
                throw new ConflictException(
                    "WORK_ITEM_DEPENDENCY_EXISTS",
                    "The dependency already exists.");
            }

            await EnsureNoDependencyPathAsync(
                projectId,
                dependency.Value.To,
                dependency.Value.From,
                ct);
        }
        else if (await edges.ExistsByFilterAsync(
                     edge => edge.ProjectId == projectId
                         && edge.SourceWorkItemId == sourceWorkItemId
                         && edge.TargetWorkItemId == targetWorkItemId
                         && edge.RelationType == relationType,
                     ct))
        {
            throw new ConflictException("WORK_ITEM_RELATION_EXISTS", "Work item relation already exists.");
        }

        await edges.CreateAsync(new WorkItemRelationEdgeDocument
        {
            Id = EdgeId(projectId, sourceWorkItemId, targetWorkItemId, relationType),
            ProjectId = projectId,
            SourceWorkItemId = sourceWorkItemId,
            TargetWorkItemId = targetWorkItemId,
            RelationType = relationType,
            DependencyFromWorkItemId = dependency?.From,
            DependencyToWorkItemId = dependency?.To,
            CreatedAt = clock.UtcNow
        }, ct);
    }

    public Task<long> RemoveRelationAsync(
        string projectId,
        string sourceWorkItemId,
        string targetWorkItemId,
        string relationType,
        CancellationToken ct) =>
        edges.DeleteByFilterAsync(
            edge => edge.ProjectId == projectId
                && edge.SourceWorkItemId == sourceWorkItemId
                && edge.TargetWorkItemId == targetWorkItemId
                && edge.RelationType == relationType,
            ct);

    public async Task<IReadOnlyList<string>> ActiveBlockerIdsAsync(
        string projectId,
        string blockedWorkItemId,
        CancellationToken ct)
    {
        var count = await edges.CountByFilterAsync(
            edge => edge.ProjectId == projectId
                && edge.DependencyToWorkItemId == blockedWorkItemId,
            ct);
        if (count > Options.MaxOutgoingDependenciesPerNode)
        {
            throw GraphLimit();
        }

        var incoming = await edges.ListByFilterAsync(
            edge => edge.ProjectId == projectId
                && edge.DependencyToWorkItemId == blockedWorkItemId,
            pageSize: Options.MaxOutgoingDependenciesPerNode,
            cancellationToken: ct);
        var blockers = new List<string>();
        foreach (var blockerId in incoming
                     .Select(edge => edge.DependencyFromWorkItemId!)
                     .Distinct(StringComparer.Ordinal))
        {
            var blocker = await workItems.SelectAsync(
                item => item.Id == blockerId && item.ProjectId == projectId,
                ct);
            if (blocker is not null
                && !blocker.Archived
                && blocker.CompletedAt is null
                && blocker.Status is not ("Done" or "Closed"))
            {
                blockers.Add(blocker.Id);
            }
        }

        return blockers;
    }

    public async Task EnsureCanSetParentAsync(
        string projectId,
        string? childWorkItemId,
        string parentWorkItemId,
        CancellationToken ct)
    {
        var childCount = childWorkItemId is null
            ? await workItems.CountByFilterAsync(
                item => item.ProjectId == projectId && item.ParentId == parentWorkItemId && !item.Archived,
                ct)
            : await workItems.CountByFilterAsync(
                item => item.ProjectId == projectId
                    && item.ParentId == parentWorkItemId
                    && item.Id != childWorkItemId
                    && !item.Archived,
                ct);
        if (childCount >= Options.MaxChildrenPerWorkItem)
        {
            throw new ConflictException(
                "WORK_ITEM_CHILD_LIMIT",
                "The parent work item child limit has been reached.");
        }

        if (childWorkItemId is null)
        {
            return;
        }

        var currentId = parentWorkItemId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var depth = 0; ; depth++)
        {
            if (currentId == childWorkItemId)
            {
                throw new ConflictException(
                    "WORK_ITEM_HIERARCHY_CYCLE",
                    "The parent assignment would create a hierarchy cycle.");
            }
            if (!visited.Add(currentId))
            {
                throw new ConflictException(
                    "WORK_ITEM_HIERARCHY_CYCLE",
                    "The existing hierarchy contains a cycle.");
            }
            if (visited.Count > Options.MaxVisitedNodes || depth >= Options.MaxTraversalDepth)
            {
                throw GraphLimit();
            }

            var current = await workItems.SelectAsync(
                item => item.Id == currentId && item.ProjectId == projectId && !item.Archived,
                ct);
            if (current?.ParentId is null)
            {
                return;
            }

            currentId = current.ParentId;
        }
    }

    private async Task EnsureNoDependencyPathAsync(
        string projectId,
        string startWorkItemId,
        string soughtWorkItemId,
        CancellationToken ct)
    {
        var pending = new Queue<(string WorkItemId, int Depth)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Enqueue((startWorkItemId, 0));
        while (pending.TryDequeue(out var current))
        {
            if (current.WorkItemId == soughtWorkItemId)
            {
                throw new ConflictException(
                    "WORK_ITEM_DEPENDENCY_CYCLE",
                    "The dependency would create a cycle.");
            }
            if (!visited.Add(current.WorkItemId))
            {
                continue;
            }
            if (visited.Count > Options.MaxVisitedNodes || current.Depth >= Options.MaxTraversalDepth)
            {
                throw GraphLimit();
            }

            var currentWorkItem = await workItems.SelectAsync(
                item => item.Id == current.WorkItemId
                    && item.ProjectId == projectId
                    && !item.Archived,
                ct);
            if (currentWorkItem is null)
            {
                continue;
            }

            var outgoingCount = await edges.CountByFilterAsync(
                edge => edge.ProjectId == projectId
                    && edge.DependencyFromWorkItemId == current.WorkItemId,
                ct);
            if (outgoingCount > Options.MaxOutgoingDependenciesPerNode)
            {
                throw GraphLimit();
            }

            var outgoing = await edges.ListByFilterAsync(
                edge => edge.ProjectId == projectId
                    && edge.DependencyFromWorkItemId == current.WorkItemId,
                pageSize: Options.MaxOutgoingDependenciesPerNode,
                cancellationToken: ct);
            foreach (var target in outgoing
                         .Select(edge => edge.DependencyToWorkItemId!)
                         .Distinct(StringComparer.Ordinal))
            {
                pending.Enqueue((target, current.Depth + 1));
            }
        }
    }

    private static (string From, string To)? DependencyDirection(
        string sourceWorkItemId,
        string targetWorkItemId,
        string relationType) => relationType switch
    {
        "Blocks" => (sourceWorkItemId, targetWorkItemId),
        "BlockedBy" => (targetWorkItemId, sourceWorkItemId),
        _ => null
    };

    public static string EdgeId(
        string projectId,
        string sourceWorkItemId,
        string targetWorkItemId,
        string relationType)
    {
        var value = Encoding.UTF8.GetBytes(
            $"{projectId}\n{sourceWorkItemId}\n{targetWorkItemId}\n{relationType}");
        return Convert.ToHexString(MD5.HashData(value)).ToLowerInvariant();
    }

    private static ConflictException GraphLimit() => new(
        "WORK_ITEM_GRAPH_LIMIT",
        "The work item graph traversal limit was reached.");
}
