namespace Zumbo.Modules.WorkItems;

public sealed record CreateWorkItemBulkJobRequest(
    string ProjectId,
    string Operation,
    IReadOnlyCollection<string> WorkItemIds,
    string? Value = null,
    bool DryRun = false);
