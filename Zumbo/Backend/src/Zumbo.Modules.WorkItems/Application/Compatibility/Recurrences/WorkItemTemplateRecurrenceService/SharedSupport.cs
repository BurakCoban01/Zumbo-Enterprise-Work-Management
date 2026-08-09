using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService
{
    private async Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken ct)
    {
        var options = lockOptions.Value;
        return await distributedLocks.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The requested resource is busy; retry the operation.");
    }

    private async Task<ProjectResourceAuthorization> EnsurePermissionAsync(
        string projectId,
        string permission,
        CancellationToken ct) =>
        await permissionChecker.EnsureCanAsync(RequireCurrentUser(), projectId, permission, ct);

    private string RequireCurrentUser() =>
        currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required.");
}
