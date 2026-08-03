using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;

public sealed class WorkflowAuditWriterAdapter(AuditService audit) : IWorkflowAuditWriter
{
    public Task WriteAsync(
        string projectId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync("WorkflowUpdated", "Project", projectId, oldValue, newValue, correlationId, ct);
}
