using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public interface IDashboardAuditWriter
{
    Task WriteAsync(
        string action,
        string dashboardId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}
