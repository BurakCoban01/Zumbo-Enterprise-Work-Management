using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
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

public sealed class BoardAuditWriterAdapter(AuditService audit) : IBoardAuditWriter
{
    public Task WriteAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(action, "Board", entityId, oldValue, newValue, correlationId, ct);
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
