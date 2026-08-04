using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public interface IAutomationProjectAccessChecker
{
    Task<AutomationProjectScope> EnsureCanViewAsync(string projectId, CancellationToken ct);
    Task<AutomationProjectScope> EnsureCanManageAsync(string projectId, CancellationToken ct);
}
