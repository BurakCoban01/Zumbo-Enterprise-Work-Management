using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

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
