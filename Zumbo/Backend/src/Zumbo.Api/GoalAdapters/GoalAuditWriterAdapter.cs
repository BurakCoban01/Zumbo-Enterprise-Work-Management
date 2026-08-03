using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

public sealed class GoalAuditWriterAdapter(AuditService audit) : IGoalAuditWriter
{
    public Task WriteAsync(
        string action,
        string goalId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(
            action,
            "Goal",
            goalId,
            oldValue,
            newValue,
            correlationId,
            ct);
}
