using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record ValidatedWorkItemShape(
    string IssueTypeKey,
    string HierarchyLevel,
    int SchemaVersion,
    IReadOnlyCollection<WorkItemCustomFieldValueDocument> CustomFields);
