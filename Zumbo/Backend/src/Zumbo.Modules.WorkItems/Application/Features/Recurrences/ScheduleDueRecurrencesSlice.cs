using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class ScheduleDueRecurrencesSlice(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
    IWorkItemRecurrenceEventPublisher recurrencePublisher,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IOptions<WorkItemRecurrenceOptions> options,
    IClock clock)
{
    private readonly RecurrenceSchedulerAccess access =
        new(recurrences, distributedLocks, lockOptions);

    internal async Task<int> HandleAsync(
        ScheduleDueRecurrencesCommand command,
        CancellationToken ct)
    {
        _ = command;
        await using var dispatcherLock = await access.AcquireAsync(
            "work-item-recurrence-scheduler",
            ct);
        var now = clock.UtcNow;
        var batchSize = Math.Clamp(options.Value.BatchSize, 1, 200);
        var candidates = await recurrences.ListByFilterAsync(
            recurrence => recurrence.Active
                && !recurrence.Archived
                && recurrence.NextRunAtUtc != null
                && recurrence.NextRunAtUtc <= now,
            recurrence => recurrence.NextRunAtUtc!,
            pageSize: batchSize,
            cancellationToken: ct);
        var scheduled = 0;
        foreach (var candidate in candidates)
        {
            await using var recurrenceLock = await access.AcquireAsync(
                "work-item-recurrence:" + candidate.Id,
                ct);
            var recurrence = await recurrences.SelectAsync(item => item.Id == candidate.Id, ct);
            if (recurrence is null
                || !recurrence.Active
                || recurrence.Archived
                || recurrence.NextRunAtUtc is null
                || recurrence.NextRunAtUtc > now)
            {
                continue;
            }

            var template = await templates.SelectAsync(
                item => item.Id == recurrence.TemplateId
                    && item.OrganizationId == recurrence.OrganizationId
                    && item.ProjectId == recurrence.ProjectId
                    && !item.Archived,
                ct);
            if (template is null)
            {
                recurrence.Active = false;
                recurrence.UpdatedAt = now;
                await access.ReplaceAsync(recurrence, ct);
                continue;
            }

            var scheduledFor = recurrence.NextRunAtUtc.Value.ToUniversalTime();
            var occurrence = await occurrences.SelectAsync(
                item => item.RecurrenceId == recurrence.Id
                    && item.ScheduledForUtc == scheduledFor,
                ct);
            if (occurrence is null)
            {
                var occurrenceId = RecurrenceOccurrenceIdentity.Create(recurrence.Id, scheduledFor);
                occurrence = await occurrences.CreateAsync(new WorkItemRecurrenceOccurrenceDocument
                {
                    Id = occurrenceId,
                    OrganizationId = recurrence.OrganizationId,
                    ProjectId = recurrence.ProjectId,
                    RecurrenceId = recurrence.Id,
                    TemplateId = recurrence.TemplateId,
                    ScheduledForUtc = scheduledFor,
                    CreatedAt = now
                }, ct);
            }

            await recurrencePublisher.PublishAsync(new WorkItemRecurrenceDueEvent(
                recurrence.OrganizationId,
                recurrence.ProjectId,
                recurrence.Id,
                occurrence.Id,
                scheduledFor), ct);
            recurrence.ScheduledOccurrences = checked(recurrence.ScheduledOccurrences + 1);
            var next = RecurrenceSchedulePolicy.Next(
                scheduledFor,
                recurrence.Frequency,
                recurrence.Interval);
            if (recurrence.ScheduledOccurrences >= recurrence.MaxOccurrences
                || recurrence.EndAtUtc is { } endAt && next > endAt)
            {
                recurrence.Active = false;
                recurrence.NextRunAtUtc = null;
            }
            else
            {
                recurrence.NextRunAtUtc = next;
            }
            recurrence.UpdatedAt = now;
            await access.ReplaceAsync(recurrence, ct);
            scheduled++;
        }
        return scheduled;
    }
}
