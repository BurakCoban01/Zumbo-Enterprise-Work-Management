using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemTypeSchemaDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public List<IssueTypeDefinitionDocument> IssueTypes { get; set; } = [];
    public List<CustomFieldDefinitionDocument> CustomFields { get; set; } = [];
    public List<IssueTypeLayoutDocument> Layouts { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
