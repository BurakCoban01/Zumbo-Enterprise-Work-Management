namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemBulkJobDueEvent(
    string OrganizationId,
    string ProjectId,
    string JobId,
    string RequestedByUserId,
    int DispatchSequence);
