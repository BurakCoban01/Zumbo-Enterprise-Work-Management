using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    public async Task<AutomationRunResponse> GetAsync(string runId, CancellationToken ct)
    {
        var run = await GetRunAsync(runId, ct);
        var scope = await access.EnsureCanViewAsync(run.ProjectId, ct);
        EnsureTenant(run, scope.OrganizationId);
        return ToResponse(run);
    }
}
