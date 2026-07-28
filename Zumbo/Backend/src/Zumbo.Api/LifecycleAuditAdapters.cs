using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;

public sealed class TeamAuditWriterAdapter(AuditService audit) : ITeamAuditWriter
{
    public Task WriteAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(action, "Team", entityId, oldValue, newValue, correlationId, ct);
}

public sealed class ProjectAuditWriterAdapter(AuditService audit) : IProjectAuditWriter
{
    public Task WriteAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(action, "Project", entityId, oldValue, newValue, correlationId, ct);
}

public sealed class BoardAuditWriterAdapter(WriteAuditLogHandler handler) : IBoardAuditWriter
{
    public async Task WriteAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct)
    {
        await handler.HandleAsync(
            new WriteAuditLogCommand(action, "Board", entityId, oldValue, newValue, correlationId),
            ct);
    }
}

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

public sealed class OrganizationAuditWriterAdapter(AuditService audit) : IOrganizationAuditWriter
{
    public Task WriteAsync(
        string action,
        string organizationId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(action, "Organization", organizationId, oldValue, newValue, correlationId, ct);
}

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
