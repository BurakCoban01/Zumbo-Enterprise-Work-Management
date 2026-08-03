using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class CapacityPlanningAuditWriterAdapter(AuditService audit)
    : ICapacityPlanningAuditWriter
{
    public Task WriteAsync(
        string action,
        string planId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(
            action,
            "CapacityPlan",
            planId,
            oldValue,
            newValue,
            correlationId,
            ct);
}
