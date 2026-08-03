using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class PortfolioAuditWriterAdapter(AuditService audit) : IPortfolioAuditWriter
{
    public Task WriteAsync(
        string action,
        string portfolioId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(
            action,
            "Portfolio",
            portfolioId,
            oldValue,
            newValue,
            correlationId,
            ct);
}
