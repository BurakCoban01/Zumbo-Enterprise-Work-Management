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

    private async Task<WorkItemRecurrenceResponse> ToResponseAsync(
        WorkItemRecurrenceDocument recurrence,
        CancellationToken ct)
    {
        var generated = await occurrences.CountByFilterAsync(
            occurrence => occurrence.RecurrenceId == recurrence.Id
                && occurrence.Status == WorkItemRecurrenceOccurrenceStates.Generated,
            ct);
        return new WorkItemRecurrenceResponse(
            recurrence.Id,
            recurrence.ProjectId,
            recurrence.TemplateId,
            recurrence.Frequency,
            recurrence.Interval,
            recurrence.StartAtUtc,
            recurrence.EndAtUtc,
            recurrence.NextRunAtUtc,
            recurrence.MaxOccurrences,
            recurrence.ScheduledOccurrences,
            generated,
            recurrence.Active,
            recurrence.Archived,
            recurrence.Version);
    }
}
