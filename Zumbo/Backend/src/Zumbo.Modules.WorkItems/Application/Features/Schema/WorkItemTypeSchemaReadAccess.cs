using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

internal sealed class WorkItemTypeSchemaReadAccess(
    IDocumentRepository<WorkItemTypeSchemaDocument> schemas,
    IDocumentRepository<WorkItemDocument> workItems,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser,
    IOptions<WorkItemTypeSchemaOptions> configuredOptions,
    IClock clock)
{
    private int BatchSize => Math.Clamp(configuredOptions.Value.BatchSize, 1, 200);
    private int MaxBatches => Math.Clamp(configuredOptions.Value.MaxBatchesPerValidation, 1, 10_000);

    internal async Task EnsureViewAsync(string projectId, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }
        _ = await permissionChecker.EnsureCanAsync(userId, projectId, PermissionCatalog.WorkItemView, ct);
    }

    internal async Task<WorkItemTypeSchemaDocument> LoadOrDefaultAsync(
        string projectId,
        CancellationToken ct) =>
        await schemas.SelectAsync(schema => schema.ProjectId == projectId, ct)
        ?? Default(projectId, clock.UtcNow);

    internal async Task<WorkItemFieldDistributionResponse> BuildDistributionAsync(
        string projectId,
        string field,
        Func<WorkItemDocument, string?> selector,
        CancellationToken ct)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var missing = 0;
        string? cursor = null;
        for (var batch = 0; ; batch++)
        {
            if (batch >= MaxBatches)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_REPORT_LIMIT",
                    "Work item field report exceeded the configured batch limit.");
            }

            var page = await workItems.ListByCursorAsync(
                item => item.ProjectId == projectId && !item.Archived,
                cursor,
                BatchSize,
                ct);
            foreach (var item in page.Items)
            {
                total++;
                var value = selector(item);
                if (string.IsNullOrWhiteSpace(value))
                {
                    missing++;
                }
                else
                {
                    counts[value] = counts.GetValueOrDefault(value) + 1;
                }
            }

            cursor = page.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        return new WorkItemFieldDistributionResponse(
            projectId,
            field,
            total,
            missing,
            counts.OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => new WorkItemFieldDistributionEntry(item.Key, item.Value))
                .ToList());
    }

    private static WorkItemTypeSchemaDocument Default(string projectId, DateTimeOffset now)
    {
        var issueTypes = new[]
        {
            new IssueTypeDefinitionDocument { Key = "Epic", Name = "Epic", HierarchyLevel = IssueTypeHierarchyLevels.Epic, Position = 0 },
            new IssueTypeDefinitionDocument { Key = "Story", Name = "Story", HierarchyLevel = IssueTypeHierarchyLevels.Standard, Position = 10 },
            new IssueTypeDefinitionDocument { Key = "Task", Name = "Task", HierarchyLevel = IssueTypeHierarchyLevels.Standard, Position = 20 },
            new IssueTypeDefinitionDocument { Key = "Bug", Name = "Bug", HierarchyLevel = IssueTypeHierarchyLevels.Standard, Position = 30 },
            new IssueTypeDefinitionDocument { Key = "Subtask", Name = "Subtask", HierarchyLevel = IssueTypeHierarchyLevels.Subtask, Position = 40 }
        };
        return new WorkItemTypeSchemaDocument
        {
            Id = projectId,
            ProjectId = projectId,
            SchemaVersion = 1,
            IssueTypes = issueTypes.ToList(),
            Layouts = issueTypes.Select(item => new IssueTypeLayoutDocument { IssueTypeKey = item.Key }).ToList(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
