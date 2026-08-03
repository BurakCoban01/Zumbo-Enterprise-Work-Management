using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    public async Task<WorkItemResponse> CreateAsync(
        CreateWorkItemRequest request,
        string correlationId,
        CancellationToken ct,
        string? requestedId = null)
    {
        var authorization = await EnsurePermissionAsync(request.ProjectId, "WorkItemCreate", ct);
        return await CreateCoreAsync(
            request,
            authorization.OrganizationId,
            correlationId,
            ct,
            requestedId,
            currentUser.UserId ?? "system",
            null,
            []);
    }

    async Task<WorkItemResponse> IIntakeWorkItemCreator.CreateAsync(
        IntakeWorkItemCreation creation,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(creation.OrganizationId)
            || string.IsNullOrWhiteSpace(creation.SubmissionId))
        {
            throw new ValidationException("Intake work creation requires organization and submission scope.");
        }

        authorizedOrganizationIds[creation.Request.ProjectId] = creation.OrganizationId;
        var requestedId = IntakeStableIds.WorkItemId(creation.SubmissionId);
        var existing = await workItems.SelectAsync(x => x.Id == requestedId, ct);
        if (existing is not null)
        {
            if (existing.ProjectId != creation.Request.ProjectId
                || existing.SourceIntakeSubmissionId != creation.SubmissionId)
            {
                throw new ConflictException(
                    "INTAKE_WORK_ITEM_ID_CONFLICT",
                    "The intake submission work item id is already in use.");
            }

            await activityStore.HydrateAsync(existing, creation.OrganizationId, ct);
            return ToResponse(existing);
        }

        return await CreateCoreAsync(
            creation.Request,
            creation.OrganizationId,
            creation.CorrelationId,
            ct,
            requestedId,
            currentUser.UserId ?? "intake",
            creation.SubmissionId,
            creation.Attachments,
            creation.Description);
    }

    private async Task<WorkItemResponse> CreateCoreAsync(
        CreateWorkItemRequest request,
        string organizationId,
        string correlationId,
        CancellationToken ct,
        string? requestedId,
        string actorUserId,
        string? intakeSubmissionId,
        IReadOnlyCollection<StoredAttachment> initialAttachments,
        string description = "")
    {
        CreateWorkItemValidator.Validate(request);

        var shape = await typeSchemas.ValidateAsync(request.ProjectId, request.Type, request.CustomFields, ct);
        var type = shape.IssueTypeKey;
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + request.ProjectId, ct);
        var parent = await ValidateParentAsync(request.ProjectId, request.BoardId, type, request.ParentId, null, ct);
        var teamId = NormalizeOptionalId(request.TeamId);
        if (teamId is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(request.ProjectId, teamId, request.AssigneeUserId, ct);
        }
        var placement = await boardPlacementPolicy.ResolveInitialAsync(request.ProjectId, request.BoardId, ct);
        var rank = await ranks.NextRankAsync(request.BoardId, placement.ColumnId, null, ct);
        var now = clock.UtcNow;
        var workItem = new WorkItemDocument
        {
            Id = string.IsNullOrWhiteSpace(requestedId) ? Guid.NewGuid().ToString("N") : requestedId,
            ProjectId = request.ProjectId,
            BoardId = request.BoardId,
            ParentId = parent?.Id,
            TeamId = teamId,
            ColumnId = placement.ColumnId,
            Title = request.Title.Trim(),
            Description = description.Trim(),
            Type = type,
            IssueTypeSchemaVersion = shape.SchemaVersion,
            CustomFields = shape.CustomFields.ToList(),
            Priority = string.IsNullOrWhiteSpace(request.Priority) ? "Medium" : request.Priority,
            Status = placement.Status,
            Rank = rank,
            AssigneeUserId = request.AssigneeUserId,
            DueDate = request.DueDate,
            SourceIntakeSubmissionId = intakeSubmissionId,
            CreatedAt = now,
            UpdatedAt = now,
            ActivityStorageVersion = 1,
            StatusHistory =
            [
                new WorkItemStatusHistoryDocument
                {
                    ToStatus = placement.Status,
                    ChangedByUserId = actorUserId,
                    ChangedAt = now
                }
            ],
            Attachments = initialAttachments.Select(stored => new AttachmentDocument
            {
                FileName = stored.FileName,
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                StoragePath = stored.StoragePath,
                ChecksumSha256 = stored.ChecksumSha256,
                SecurityState = stored.SecurityState,
                ScanProvider = stored.ScanProvider,
                ScanDetail = stored.ScanDetail,
                ScannedAt = stored.ScannedAt,
                CreatedAt = now
            }).ToList()
        };

        await using (await AcquirePlacementLockAsync(request.BoardId, placement, ct))
        {
            if (wipProjection is null)
            {
                await boardPlacementPolicy.EnsureHasCapacityAsync(request.BoardId, placement.ColumnId, null, ct);
            }
            else
            {
                await wipProjection.ReserveCreateAsync(request.ProjectId, request.BoardId, placement, ct);
            }
            var initialTimeline = workItem.StatusHistory;
            var separatedAttachments = workItem.Attachments;
            workItem.StatusHistory = [];
            workItem.Attachments = [];
            try
            {
                await workItems.CreateAsync(workItem, ct);
            }
            finally
            {
                workItem.StatusHistory = initialTimeline;
                workItem.Attachments = separatedAttachments;
            }
            await activityStore.CreateTimelineAsync(
                WorkItemActivityStore.ToActivity(workItem, organizationId, workItem.StatusHistory[0], 0),
                ct);
            foreach (var attachment in workItem.Attachments)
            {
                await activityStore.CreateAttachmentAsync(
                    WorkItemActivityStore.ToActivity(workItem, organizationId, attachment),
                    ct);
            }
        }
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemCreated", "WorkItem", workItem.Id, null, workItem.Title, correlationId, ct);
        if (collaborationService is not null)
        {
            await collaborationService.RecordActivityAsync(
                workItem, organizationId, "WorkItemCreated", "Work item created", correlationId, ct);
        }
        await PublishRealtimeAsync("created", workItem, correlationId, ct);

        if (!string.IsNullOrWhiteSpace(workItem.AssigneeUserId))
        {
            await notifications.NotifyAsync(workItem.AssigneeUserId, "Assignment", $"Assigned to {workItem.Title}", ct);
        }

        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        await PublishAutomationAsync(
            "WorkItemCreated",
            workItem,
            previousStatus: null,
            correlationId,
            $"created:{workItem.Version}",
            ct);
        return ToResponse(workItem);
    }
}
