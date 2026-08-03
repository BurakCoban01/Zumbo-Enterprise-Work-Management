using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    private async Task<AutomationRunDocument> GetRunAsync(string runId, CancellationToken ct) =>
        await runs.SelectAsync(run => run.Id == runId, ct)
        ?? throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");
}
