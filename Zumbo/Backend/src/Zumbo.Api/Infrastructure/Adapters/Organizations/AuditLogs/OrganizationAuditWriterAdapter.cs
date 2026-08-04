using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;

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
