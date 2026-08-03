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

    private async Task<WorkItemRecurrenceDocument> GetRecurrenceAsync(
        string recurrenceId,
        bool includeArchived,
        CancellationToken ct) =>
        await recurrences.SelectAsync(
            recurrence => recurrence.Id == recurrenceId && (includeArchived || !recurrence.Archived), ct)
        ?? throw new NotFoundException("WORK_ITEM_RECURRENCE_NOT_FOUND", "Work item recurrence was not found.");
}
