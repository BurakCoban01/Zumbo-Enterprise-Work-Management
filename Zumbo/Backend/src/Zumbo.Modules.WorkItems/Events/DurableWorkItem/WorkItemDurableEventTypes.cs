using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public static class WorkItemDurableEventTypes
{
    public const string Audit = "work-item.audit.v1";
    public const string Notification = "work-item.notification.v1";
    public const string SearchUpsert = "work-item.search-upsert.v1";
    public const string SearchDelete = "work-item.search-delete.v1";
    public const string Realtime = "work-item.realtime.v1";
    public const string CacheInvalidation = "work-item.cache-invalidation.v1";
    public const string Webhook = "work-item.webhook.v1";
    public const string RecurrenceOccurrence = "work-item.recurrence-occurrence.v1";
    public const string BulkJob = "work-item.bulk-job.v1";
    public const string Automation = "work-item.automation.v1";
    public const string DevelopmentWebhook = "work-item.development-webhook.v1";
}
