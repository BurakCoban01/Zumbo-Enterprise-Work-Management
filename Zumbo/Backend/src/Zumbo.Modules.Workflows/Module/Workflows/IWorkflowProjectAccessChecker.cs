using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public interface IWorkflowProjectAccessChecker
{
    Task EnsureCanViewAsync(string projectId, CancellationToken ct);
    Task EnsureCanManageAsync(string projectId, CancellationToken ct);
}
