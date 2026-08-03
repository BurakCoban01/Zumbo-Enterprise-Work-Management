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

    private async Task ReplaceRecurrenceAsync(WorkItemRecurrenceDocument recurrence, CancellationToken ct)
    {
        var result = await recurrences.ReplaceByVersionAsync(
            x => x.Id == recurrence.Id, recurrence, recurrence.Version, ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_CONFLICT", "The recurrence changed concurrently; retry the operation.");
        }
        recurrence.Version = result.Version!.Value;
    }
}
