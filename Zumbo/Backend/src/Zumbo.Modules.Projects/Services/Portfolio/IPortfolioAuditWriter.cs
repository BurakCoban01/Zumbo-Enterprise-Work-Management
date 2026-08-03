using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public interface IPortfolioAuditWriter
{
    Task WriteAsync(
        string action,
        string portfolioId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}
