using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;

namespace Zumbo.RepositoryContracts;

public abstract class WorkItemBulkJobRepositoryContract
{
    protected abstract IDocumentRepository<WorkItemBulkJobDocument> Jobs();
    protected abstract IDocumentRepository<WorkItemBulkJobItemDocument> Items();

    [Fact]
    public async Task BulkJobStores_PreserveTenantCasCheckpointAndItemDedupe()
    {
        var jobs = Jobs();
        var items = Items();
        var prefix = "domain010-contract-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var job = new WorkItemBulkJobDocument
        {
            Id = prefix + "-job", OrganizationId = prefix + "-org", ProjectId = prefix + "-project",
            RequestedByUserId = prefix + "-user", Type = WorkItemBulkJobTypes.Import,
            IdempotencyKeyHash = prefix + "-key", RequestFingerprint = prefix + "-fingerprint",
            TotalItems = 2, CreatedAt = now, UpdatedAt = now
        };
        try
        {
            job = await jobs.CreateAsync(job);
            var first = await items.CreateAsync(new WorkItemBulkJobItemDocument
            {
                Id = WorkItemBulkJobService.StableItemId(job.Id, 0), OrganizationId = job.OrganizationId,
                ProjectId = job.ProjectId, JobId = job.Id, ItemIndex = 0, SourceKey = "one", PayloadJson = "{}"
            });
            await items.CreateAsync(new WorkItemBulkJobItemDocument
            {
                Id = WorkItemBulkJobService.StableItemId(job.Id, 1), OrganizationId = job.OrganizationId,
                ProjectId = job.ProjectId, JobId = job.Id, ItemIndex = 1, SourceKey = "two", PayloadJson = "{}"
            });

            var stale = await jobs.SelectAsync(x => x.Id == job.Id);
            job.State = WorkItemBulkJobStates.Running;
            job.DispatchSequence = 2;
            var replaced = await jobs.ReplaceByVersionAsync(x => x.Id == job.Id, job, job.Version);
            Assert.True(replaced.Found);
            stale!.State = WorkItemBulkJobStates.Cancelled;
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                jobs.ReplaceByVersionAsync(x => x.Id == stale.Id, stale, stale.Version));

            Assert.Null(await jobs.SelectAsync(x => x.Id == job.Id && x.OrganizationId == prefix + "-foreign"));
            var pending = await items.ListByFilterAsync(
                x => x.JobId == job.Id && x.State == WorkItemBulkJobItemStates.Pending,
                x => x.ItemIndex, pageSize: 1);
            Assert.Equal(first.Id, Assert.Single(pending).Id);
            await Assert.ThrowsAsync<DocumentConflictException>(() => items.CreateAsync(new WorkItemBulkJobItemDocument
            {
                Id = first.Id, OrganizationId = job.OrganizationId, ProjectId = job.ProjectId,
                JobId = job.Id, ItemIndex = 0, SourceKey = "duplicate", PayloadJson = "{}"
            }));
        }
        finally
        {
            await items.DeleteByFilterAsync(x => x.JobId == job.Id);
            await jobs.DeleteByFilterAsync(x => x.Id == job.Id);
        }
    }
}
