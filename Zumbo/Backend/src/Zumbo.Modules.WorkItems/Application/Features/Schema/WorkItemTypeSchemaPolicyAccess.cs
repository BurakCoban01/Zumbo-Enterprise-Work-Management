using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

internal sealed class WorkItemTypeSchemaPolicyAccess(
    IDocumentRepository<WorkItemTypeSchemaDocument> schemas,
    IClock clock)
{
    internal async Task<WorkItemTypeSchemaDocument> LoadOrDefaultAsync(
        string projectId,
        CancellationToken ct) =>
        await schemas.SelectAsync(schema => schema.ProjectId == projectId, ct)
        ?? Default(projectId, clock.UtcNow);

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
