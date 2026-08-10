using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService
{
    private async Task<IAsyncDisposable> AcquireProjectLockAsync(string projectId, CancellationToken ct)
    {
        var lease = TimeSpan.FromSeconds(Math.Clamp(lockOptions.Value.LeaseSeconds, 5, 300));
        var wait = TimeSpan.FromSeconds(Math.Clamp(lockOptions.Value.WaitSeconds, 0, 30));
        return await distributedLocks.TryAcquireAsync("project-structure:" + projectId, lease, wait, ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The project structure is busy; retry the operation.");
    }

    private async Task<SprintDocument> GetSprintAsync(string sprintId, CancellationToken ct) =>
        await sprints.SelectAsync(sprint => sprint.Id == sprintId, ct)
        ?? throw new NotFoundException("SPRINT_NOT_FOUND", "Sprint was not found.");

    private async Task SaveSprintAsync(SprintDocument sprint, CancellationToken ct)
    {
        var expectedVersion = expectedVersions?.ExpectedVersion ?? sprint.Version;
        var result = await sprints.ReplaceByVersionAsync(x => x.Id == sprint.Id, sprint, expectedVersion, ct);
        if (!result.Found)
        {
            throw new ConflictException("SPRINT_CONCURRENCY_CONFLICT", "Sprint changed concurrently; reload and retry.");
        }

        sprint.Version = result.Version!.Value;
    }

    private async Task SaveWorkItemAsync(WorkItemDocument item, bool useRequestVersion, CancellationToken ct)
    {
        var expectedVersion = useRequestVersion
            ? expectedVersions?.ExpectedVersion ?? item.Version
            : item.Version;
        var result = await workItems.ReplaceByVersionAsync(x => x.Id == item.Id, item, expectedVersion, ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_CONCURRENCY_CONFLICT", "Work item changed concurrently; reload and retry.");
        }

        item.Version = result.Version!.Value;
    }
}
