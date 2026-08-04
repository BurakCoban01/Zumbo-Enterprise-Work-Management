using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class DashboardAuditWriterAdapter(AuditService audit) : IDashboardAuditWriter
{
    public Task WriteAsync(
        string action,
        string dashboardId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(
            action,
            "Dashboard",
            dashboardId,
            oldValue,
            newValue,
            correlationId,
            ct);
}
