using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class IntakeFormVersionDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string FormId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public int DefinitionVersion { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IntakeFormDefinitionDocument Definition { get; set; } = new();
    public string PublishedByUserId { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
    public long Version { get; set; }
}
