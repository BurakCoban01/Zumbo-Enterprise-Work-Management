using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    private async Task ReplaceRunAsync(
        AutomationRunDocument run,
        bool useRequestVersion,
        CancellationToken ct)
    {
        var expected = useRequestVersion
            ? expectedVersion.Consume(run.Version)
            : run.Version;
        var result = await runs.ReplaceByVersionAsync(
            candidate => candidate.Id == run.Id,
            run,
            expected,
            ct);
        if (!result.Found)
            throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");
        run.Version = result.Version!.Value;
    }
}
