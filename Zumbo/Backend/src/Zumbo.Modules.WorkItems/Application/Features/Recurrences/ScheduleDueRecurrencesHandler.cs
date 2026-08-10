using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed class ScheduleDueRecurrencesHandler(WorkItemTemplateRecurrenceService service)
{
    private ScheduleDueRecurrencesSlice? slice;

    public ScheduleDueRecurrencesHandler(
        IDocumentRepository<WorkItemTemplateDocument> templates,
        IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
        IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
        IWorkItemRecurrenceEventPublisher recurrencePublisher,
        IDistributedLockProvider distributedLocks,
        IOptions<DistributedLockOptions> lockOptions,
        IOptions<WorkItemRecurrenceOptions> options,
        IClock clock)
        : this(null!)
    {
        slice = new ScheduleDueRecurrencesSlice(
            templates,
            recurrences,
            occurrences,
            recurrencePublisher,
            distributedLocks,
            lockOptions,
            options,
            clock);
    }

    public Task<int> HandleAsync(
        ScheduleDueRecurrencesCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.ScheduleDueAsync(ct);
}
