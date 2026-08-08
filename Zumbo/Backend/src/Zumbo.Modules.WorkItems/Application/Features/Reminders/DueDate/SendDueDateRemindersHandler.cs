using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class SendDueDateRemindersHandler(WorkItemService service)
{
    private SendDueDateRemindersPipeline? pipeline;

    public SendDueDateRemindersHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemNotificationPublisher notifications,
        IClock clock,
        IProjectPermissionChecker permissionChecker,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions)
        : this(null!)
    {
        pipeline = new SendDueDateRemindersPipeline(
            workItems,
            notifications,
            clock,
            permissionChecker,
            distributedLockProvider,
            distributedLockOptions,
            activityStore,
            expectedVersions);
    }

    public Task<int> HandleAsync(SendDueDateRemindersCommand command, CancellationToken ct) =>
        pipeline?.SendAsync(command, ct)
        ?? service.SendDueDateRemindersAsync(command.HorizonHours, ct);
}
