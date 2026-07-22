using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;

namespace Zumbo.RepositoryContracts;

public abstract class PrivacyWorkflowRepositoryContract
{
    protected abstract IDocumentRepository<PrivacyWorkflowDocument> Jobs();

    [Fact]
    public async Task PrivacyWorkflowStore_PreservesTenantCasCheckpointAndRetention()
    {
        var jobs = Jobs();
        var prefix = "platform006-contract-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var job = new PrivacyWorkflowDocument
        {
            Id = prefix + "-job",
            OrganizationId = prefix + "-org",
            RequestedByUserId = prefix + "-user",
            Pseudonym = "anon-" + prefix[..16],
            StatusTokenHash = new string('a', 64),
            DispatchSequence = 1,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddDays(7),
            ExpiresAtUtc = now.AddDays(7).UtcDateTime
        };
        try
        {
            job = await jobs.CreateAsync(job);
            var stale = await jobs.SelectAsync(x => x.Id == job.Id);
            job.State = PrivacyWorkflowStates.Running;
            job.CompletedSteps = 1;
            var replaced = await jobs.ReplaceByVersionAsync(
                x => x.Id == job.Id && x.OrganizationId == job.OrganizationId,
                job,
                job.Version);
            Assert.True(replaced.Found);

            stale!.State = PrivacyWorkflowStates.Failed;
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                jobs.ReplaceByVersionAsync(x => x.Id == stale.Id, stale, stale.Version));
            Assert.Null(await jobs.SelectAsync(x =>
                x.Id == job.Id && x.OrganizationId == prefix + "-foreign"));
            var ownerPage = await jobs.ListByCursorAsync(x =>
                x.OrganizationId == job.OrganizationId
                && x.RequestedByUserId == job.RequestedByUserId,
                pageSize: 1);
            Assert.Equal(job.Id, Assert.Single(ownerPage.Items).Id);
            Assert.Equal(1, await jobs.CountByFilterAsync(x => x.ExpiresAtUtc <= now.AddDays(8).UtcDateTime));
        }
        finally
        {
            await jobs.DeleteByFilterAsync(x => x.Id == job.Id);
        }
    }
}
