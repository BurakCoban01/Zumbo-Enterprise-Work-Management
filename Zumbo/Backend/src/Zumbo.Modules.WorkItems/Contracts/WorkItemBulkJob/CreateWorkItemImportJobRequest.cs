namespace Zumbo.Modules.WorkItems;

public sealed record CreateWorkItemImportJobRequest(
    string ProjectId,
    IReadOnlyCollection<WorkItemImportRow> Items,
    bool DryRun = false);
