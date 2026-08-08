using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class RecurringWorkItemGenerator(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemTeamPolicy teamPolicy,
    IWorkItemCollaboratorDirectory collaboratorDirectory,
    IBoardPlacementPolicy boardPlacementPolicy,
    IWorkItemTypeSchemaPolicy typeSchemas,
    WorkItemWipProjection wipProjection,
    WorkItemRankService ranks,
    IWorkItemActivityStore activityStore,
    WorkItemCollaborationService collaboration,
    IWorkItemSearchPublisher search,
    IWorkItemAuditPublisher audit,
    IWorkItemNotificationPublisher notifications,
    IWorkItemRealtimePublisher realtime,
    IWorkItemCacheInvalidationPublisher cache,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IClock clock)
{
    public async Task GenerateAsync(WorkItemRecurrenceDueEvent message, CancellationToken ct)
    {
        var occurrence = await occurrences.SelectAsync(x => x.Id == message.OccurrenceId, ct)
            ?? throw new NotFoundException("WORK_ITEM_RECURRENCE_OCCURRENCE_NOT_FOUND", "Recurrence occurrence was not found.");
        EnsureEventOwnership(occurrence, message);
        if (occurrence.Status == WorkItemRecurrenceOccurrenceStates.Generated)
        {
            return;
        }

        var recurrence = await recurrences.SelectAsync(x => x.Id == message.RecurrenceId, ct)
            ?? throw new NotFoundException("WORK_ITEM_RECURRENCE_NOT_FOUND", "Work item recurrence was not found.");
        if (recurrence.OrganizationId != message.OrganizationId
            || recurrence.ProjectId != message.ProjectId
            || recurrence.TemplateId != occurrence.TemplateId)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_OWNERSHIP_INVALID", "Recurrence ownership does not match the durable event.");
        }
        var template = await templates.SelectAsync(x => x.Id == recurrence.TemplateId && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_TEMPLATE_NOT_FOUND", "Work item template was not found.");
        if (template.OrganizationId != message.OrganizationId || template.ProjectId != message.ProjectId)
        {
            throw new ConflictException("WORK_ITEM_TEMPLATE_OWNERSHIP_INVALID", "Template ownership does not match the recurrence.");
        }

        var workItemId = occurrence.Id;
        var workItem = await workItems.SelectAsync(x => x.Id == workItemId, ct);
        if (workItem is null)
        {
            workItem = await CreateWorkItemAsync(template, recurrence, occurrence, ct);
        }
        else if (workItem.SourceRecurrenceId != recurrence.Id
                 || workItem.SourceTemplateId != template.Id
                 || workItem.ProjectId != recurrence.ProjectId)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_ID_COLLISION", "The deterministic recurrence work item id is already in use.");
        }

        occurrence.Status = WorkItemRecurrenceOccurrenceStates.Generated;
        occurrence.CreatedWorkItemId = workItem.Id;
        occurrence.GeneratedAt ??= clock.UtcNow;
        var result = await occurrences.ReplaceByVersionAsync(
            x => x.Id == occurrence.Id, occurrence, occurrence.Version, ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_OCCURRENCE_CONFLICT", "The occurrence changed concurrently; retry processing.");
        }
    }

    private async Task<WorkItemDocument> CreateWorkItemAsync(
        WorkItemTemplateDocument template,
        WorkItemRecurrenceDocument recurrence,
        WorkItemRecurrenceOccurrenceDocument occurrence,
        CancellationToken ct)
    {
        await using var structureLock = await AcquireAsync("project-structure:" + template.ProjectId, ct);
        var shape = await typeSchemas.ValidateAsync(
            template.ProjectId,
            template.Type,
            template.CustomFields.Select(ToRequest).ToList(),
            ct);
        if (template.TeamId is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(template.ProjectId, template.TeamId, template.AssigneeUserId, ct);
        }
        else if (template.AssigneeUserId is not null
                 && !await collaboratorDirectory.IsActiveProjectViewerAsync(
                     template.AssigneeUserId, template.OrganizationId, template.ProjectId, ct))
        {
            throw new ConflictException("WORK_ITEM_TEMPLATE_ASSIGNEE_INELIGIBLE", "Template assignee is no longer eligible for the project.");
        }

        var placement = await boardPlacementPolicy.ResolveInitialAsync(template.ProjectId, template.BoardId, ct);
        var rank = await ranks.NextRankAsync(template.BoardId, placement.ColumnId, null, ct);
        var now = clock.UtcNow;
        var workItem = new WorkItemDocument
        {
            Id = occurrence.Id,
            ProjectId = template.ProjectId,
            BoardId = template.BoardId,
            TeamId = template.TeamId,
            ColumnId = placement.ColumnId,
            Title = template.Title,
            Description = template.Description,
            Type = shape.IssueTypeKey,
            IssueTypeSchemaVersion = shape.SchemaVersion,
            CustomFields = shape.CustomFields.ToList(),
            Priority = template.Priority,
            Status = placement.Status,
            Rank = rank,
            AssigneeUserId = template.AssigneeUserId,
            DueDate = template.DueAfterDays is null
                ? null
                : occurrence.ScheduledForUtc.AddDays(template.DueAfterDays.Value),
            SourceTemplateId = template.Id,
            SourceRecurrenceId = recurrence.Id,
            RecurrenceScheduledForUtc = occurrence.ScheduledForUtc,
            Labels = [.. template.Labels],
            ActivityStorageVersion = 1,
            CreatedAt = now,
            UpdatedAt = now,
            StatusHistory =
            [
                new WorkItemStatusHistoryDocument
                {
                    ToStatus = placement.Status,
                    ChangedByUserId = "system",
                    ChangedAt = now
                }
            ]
        };

        await using (placement.EnforcesWipLimit
            ? await AcquireAsync($"board-column:{template.BoardId}:{placement.ColumnId}", ct)
            : null)
        {
            await wipProjection.ReserveCreateAsync(template.ProjectId, template.BoardId, placement, ct);
            var timeline = workItem.StatusHistory;
            workItem.StatusHistory = [];
            try
            {
                await workItems.CreateAsync(workItem, ct);
            }
            catch (DocumentConflictException)
            {
                var existing = await workItems.SelectAsync(x => x.Id == workItem.Id, ct);
                if (existing is null)
                {
                    throw;
                }
                return existing;
            }
            finally
            {
                workItem.StatusHistory = timeline;
            }
            await activityStore.CreateTimelineAsync(
                WorkItemActivityStore.ToActivity(workItem, template.OrganizationId, timeline[0], 0), ct);
        }

        var correlationId = "recurrence:" + occurrence.Id;
        await search.IndexAsync(WorkItemPublicationMapper.ToSearchRecord(workItem, template.OrganizationId), ct);
        await audit.WriteAsync(
            "RecurringWorkItemGenerated", "WorkItem", workItem.Id, template.Id, occurrence.Id, correlationId, ct);
        await collaboration.RecordActivityAsync(
            workItem,
            template.OrganizationId,
            "RecurringWorkItemGenerated",
            "Generated from recurring template",
            occurrence.Id,
            ct);
        if (workItem.AssigneeUserId is not null)
        {
            await notifications.NotifyAsync(
                workItem.AssigneeUserId,
                "Assignment",
                $"Assigned to {workItem.Title}",
                ct,
                $"recurrence-assignment:{occurrence.Id}:{workItem.AssigneeUserId}");
        }
        await realtime.PublishAsync(new WorkItemRealtimeChange(
            "created",
            workItem.Id,
            workItem.ProjectId,
            workItem.BoardId,
            WorkItemPublicationMapper.ToRealtimeItem(workItem),
            correlationId,
            now,
            WorkItemRealtimeProtocol.CurrentSchemaVersion,
            workItem.Version), ct);
        await cache.InvalidateProjectAsync(workItem.ProjectId, ct);
        return workItem;
    }

    private async Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken ct)
    {
        var options = lockOptions.Value;
        return await distributedLocks.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The requested resource is busy; retry processing.");
    }

    private static WorkItemCustomFieldValueRequest ToRequest(WorkItemCustomFieldValueDocument value) => new(
        value.FieldKey,
        value.TextValue,
        value.NumberValue,
        value.BooleanValue,
        value.DateValueUtc is null ? null : DateOnly.FromDateTime(value.DateValueUtc.Value.UtcDateTime),
        value.OptionKey);

    private static void EnsureEventOwnership(
        WorkItemRecurrenceOccurrenceDocument occurrence,
        WorkItemRecurrenceDueEvent message)
    {
        if (occurrence.OrganizationId != message.OrganizationId
            || occurrence.ProjectId != message.ProjectId
            || occurrence.RecurrenceId != message.RecurrenceId
            || occurrence.ScheduledForUtc != message.ScheduledForUtc.ToUniversalTime())
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_EVENT_INVALID", "Durable recurrence event ownership is invalid.");
        }
    }
}
