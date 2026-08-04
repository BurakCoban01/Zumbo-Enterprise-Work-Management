using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService{

    public async Task<int> ScheduleDueAsync(CancellationToken ct)
    {
        await using var dispatcherLock = await AcquireAsync("work-item-recurrence-scheduler", ct);
        var now = clock.UtcNow;
        var batchSize = Math.Clamp(Options.BatchSize, 1, 200);
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
            await using var recurrenceLock = await AcquireAsync("work-item-recurrence:" + candidate.Id, ct);
            var recurrence = await recurrences.SelectAsync(x => x.Id == candidate.Id, ct);
            if (recurrence is null
                || !recurrence.Active
                || recurrence.Archived
                || recurrence.NextRunAtUtc is null
                || recurrence.NextRunAtUtc > now)
            {
                continue;
            }

            var template = await templates.SelectAsync(
                x => x.Id == recurrence.TemplateId
                    && x.OrganizationId == recurrence.OrganizationId
                    && x.ProjectId == recurrence.ProjectId
                    && !x.Archived,
                ct);
            if (template is null)
            {
                recurrence.Active = false;
                recurrence.UpdatedAt = now;
                await ReplaceRecurrenceAsync(recurrence, ct);
                continue;
            }

            var scheduledFor = recurrence.NextRunAtUtc.Value.ToUniversalTime();
            var occurrenceId = StableOccurrenceId(recurrence.Id, scheduledFor);
            var occurrence = await occurrences.SelectAsync(x => x.Id == occurrenceId, ct);
            if (occurrence is null)
            {
                try
                {
                    await occurrences.CreateAsync(new WorkItemRecurrenceOccurrenceDocument
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
                catch (DocumentConflictException)
                {
                    occurrence = await occurrences.SelectAsync(x => x.Id == occurrenceId, ct);
                    if (occurrence is null)
                    {
                        throw;
                    }
                }
            }

            await recurrencePublisher.PublishAsync(new WorkItemRecurrenceDueEvent(
                recurrence.OrganizationId,
                recurrence.ProjectId,
                recurrence.Id,
                occurrenceId,
                scheduledFor), ct);
            recurrence.ScheduledOccurrences = checked(recurrence.ScheduledOccurrences + 1);
            var next = Next(scheduledFor, recurrence.Frequency, recurrence.Interval);
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
            await ReplaceRecurrenceAsync(recurrence, ct);
            scheduled++;
        }

        return scheduled;
    }
}
