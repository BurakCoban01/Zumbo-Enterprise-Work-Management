using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;

public sealed class AutomationAuditWriterAdapter(AuditService audit) : IAutomationAuditWriter
{
    public Task WriteAsync(
        string action,
        string ruleId,
        string projectId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(action, "AutomationRule", ruleId, oldValue, newValue, correlationId, ct);
}
