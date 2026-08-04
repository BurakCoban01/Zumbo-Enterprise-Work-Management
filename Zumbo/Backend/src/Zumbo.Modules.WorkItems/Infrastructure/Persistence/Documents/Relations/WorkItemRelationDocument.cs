using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemRelationDocument
{
    public string RelatedWorkItemId { get; set; } = string.Empty;
    public string RelationType { get; set; } = "RelatesTo";
}
