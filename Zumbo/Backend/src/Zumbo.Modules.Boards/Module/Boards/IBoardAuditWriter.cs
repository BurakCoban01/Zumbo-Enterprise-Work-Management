using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public interface IBoardAuditWriter
{
    Task WriteAsync(string action, string entityId, string? oldValue, string? newValue, string correlationId, CancellationToken ct);
}
