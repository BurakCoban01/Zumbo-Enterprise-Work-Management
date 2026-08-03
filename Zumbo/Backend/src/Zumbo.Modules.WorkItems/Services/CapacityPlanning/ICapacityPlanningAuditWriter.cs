using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public interface ICapacityPlanningAuditWriter
{
    Task WriteAsync(
        string action,
        string planId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}
