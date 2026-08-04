using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;
public sealed record WorkItemRelationResponse(string RelatedWorkItemId, string RelationType);
