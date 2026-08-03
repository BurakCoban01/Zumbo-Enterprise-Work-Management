using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public interface IWorkflowAuditWriter
{
    Task WriteAsync(string projectId, string? oldValue, string? newValue, string correlationId, CancellationToken ct);
}
