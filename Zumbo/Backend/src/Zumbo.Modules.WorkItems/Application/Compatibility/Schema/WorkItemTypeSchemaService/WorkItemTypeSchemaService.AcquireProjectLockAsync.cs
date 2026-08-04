using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    private async Task<IAsyncDisposable> AcquireProjectLockAsync(string projectId, CancellationToken ct)
    {
        var options = lockOptions.Value;
        return await distributedLocks.TryAcquireAsync(
                "work-item-schema:" + projectId,
                TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
                TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
                ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The work item schema is busy; retry the operation.");
    }
}
