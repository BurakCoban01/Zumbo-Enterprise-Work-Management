namespace Zumbo.Modules.WorkItems;

public sealed record CreateWorkItemExportJobRequest(
    string ProjectId,
    bool DryRun = false,
    bool IncludeArchived = false);
