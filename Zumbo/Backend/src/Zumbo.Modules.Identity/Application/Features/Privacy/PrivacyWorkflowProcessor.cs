using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class PrivacyWorkflowProcessor(
    IDocumentRepository<PrivacyWorkflowDocument> jobs,
    PrivacyService privacy,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IOptions<PrivacyWorkflowOptions> workflowOptions,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task ProcessAsync(PrivacyWorkflowDueEvent message, CancellationToken ct)
    {
        var lockOptions = distributedLockOptions.Value;
        await using var workflowLock = await distributedLockProvider.TryAcquireAsync(
            "privacy-workflow:" + message.JobId,
            TimeSpan.FromSeconds(Math.Clamp(workflowOptions.Value.LeaseSeconds, 30, 3600)),
            TimeSpan.FromSeconds(Math.Clamp(lockOptions.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException(
                "PRIVACY_WORKFLOW_BUSY",
                "Privacy workflow is already being processed; retry delivery later.");
        var job = await jobs.SelectAsync(x => x.Id == message.JobId, ct);
        if (job is null
            || job.OrganizationId != message.OrganizationId
            || job.RequestedByUserId != message.RequestedByUserId)
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_EVENT_INVALID",
                "Privacy workflow event ownership is invalid.");
        }
        if (currentUser.UserId != job.RequestedByUserId
            || currentUser.OrganizationId != job.OrganizationId)
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_ACTOR_INVALID",
                "Privacy workflow actor context is invalid.");
        }
        if (job.DispatchSequence != message.DispatchSequence
            || job.State is PrivacyWorkflowStates.Failed
                or PrivacyWorkflowStates.Completed
                or PrivacyWorkflowStates.Expired)
        {
            return;
        }

        try
        {
            if (job.State == PrivacyWorkflowStates.Pending)
            {
                job.State = PrivacyWorkflowStates.Running;
                job.StartedAt ??= clock.UtcNow;
                job.Attempts++;
                job.UpdatedAt = clock.UtcNow;
                await ReplaceAsync(job, ct);
            }

            if (job.CompletedSteps == 0)
            {
                await privacy.AnonymizeReferencesForWorkflowAsync(
                    job.RequestedByUserId,
                    job.Pseudonym,
                    ct);
                job.CompletedSteps = 1;
                job.UpdatedAt = clock.UtcNow;
                await ReplaceAsync(job, ct);
            }

            if (job.CompletedSteps == 1)
            {
                await privacy.FinalizeAnonymizationForWorkflowAsync(
                    job.RequestedByUserId,
                    job.Pseudonym,
                    $"privacy-workflow:{job.Id}",
                    ct);
                job.CompletedSteps = 2;
                job.State = PrivacyWorkflowStates.Completed;
                job.CompletedAt = clock.UtcNow;
                job.UpdatedAt = clock.UtcNow;
                await ReplaceAsync(job, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ConflictException exception) when (exception.Code == "PRIVACY_WORKFLOW_CONFLICT")
        {
            // Another delivery advanced the same checkpoint first.
            return;
        }
        catch (Exception exception)
        {
            var latest = await jobs.SelectAsync(x => x.Id == job.Id, ct) ?? job;
            latest.State = PrivacyWorkflowStates.Failed;
            latest.LastErrorCode = exception is ZumboException zumbo
                ? zumbo.Code
                : "PRIVACY_WORKFLOW_DEPENDENCY_FAILED";
            latest.LastErrorMessage = exception is ZumboException
                ? Limit(exception.Message)
                : "Privacy workflow dependency failed; retry or reconcile is available.";
            latest.UpdatedAt = clock.UtcNow;
            await ReplaceAsync(latest, ct);
        }
    }

    private async Task ReplaceAsync(PrivacyWorkflowDocument job, CancellationToken ct)
    {
        var result = await jobs.ReplaceByVersionAsync(
            x => x.Id == job.Id && x.OrganizationId == job.OrganizationId,
            job,
            job.Version,
            ct);
        if (!result.Found)
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_CONFLICT",
                "Privacy workflow changed concurrently.");
        }
        job.Version = result.Version!.Value;
    }

    private static string Limit(string value) => value.Length <= 500 ? value : value[..500];
}
