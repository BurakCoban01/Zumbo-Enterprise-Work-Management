using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;

public sealed class WorkItemOperationsAuditWriterAdapter(AuditService audit) : IWorkItemOperationsAuditWriter
{
    public Task WriteAsync(
        string action,
        string entityType,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(action, entityType, entityId, oldValue, newValue, correlationId, ct);
}
