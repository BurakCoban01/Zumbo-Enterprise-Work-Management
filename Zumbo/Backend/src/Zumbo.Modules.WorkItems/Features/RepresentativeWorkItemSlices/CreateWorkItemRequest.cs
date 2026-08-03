using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CreateWorkItemRequest(
    string ProjectId,
    string BoardId,
    string Title,
    string Type,
    string Priority,
    string? AssigneeUserId,
    DateTimeOffset? DueDate,
    string? ParentId = null,
    string? TeamId = null,
    IReadOnlyCollection<WorkItemCustomFieldValueRequest>? CustomFields = null);
