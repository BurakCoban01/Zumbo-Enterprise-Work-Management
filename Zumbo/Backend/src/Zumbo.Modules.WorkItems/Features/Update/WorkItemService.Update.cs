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
    public async Task<WorkItemResponse> UpdateAsync(string id, UpdateWorkItemRequest request, string correlationId, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        var oldValue = $"{workItem.Title}|{workItem.Priority}|{workItem.DueDate:o}";

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            if (request.Title.Length > 200)
            {
                throw new ValidationException("Work item title cannot exceed 200 characters.");
            }

            workItem.Title = request.Title.Trim();
        }

        if (request.Description is not null)
        {
            workItem.Description = request.Description.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Priority))
        {
            workItem.Priority = request.Priority.Trim();
        }

        if (workItem.DueDate != request.DueDate)
        {
            workItem.DueReminderSentAt = null;
        }
        workItem.DueDate = request.DueDate;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemUpdated", "WorkItem", workItem.Id, oldValue, $"{workItem.Title}|{workItem.Priority}|{workItem.DueDate:o}", correlationId, ct);
        if (collaborationService is not null)
        {
            var organizationId = CurrentOrganizationId(workItem.ProjectId);
            await collaborationService.RecordActivityAsync(
                workItem, organizationId, "WorkItemUpdated", "Fields updated", correlationId, ct);
            await collaborationService.NotifyWatchersAsync(
                workItem, organizationId, "WatcherUpdate", $"{workItem.Title} was updated", correlationId, null, ct);
        }
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        await PublishAutomationAsync(
            "WorkItemUpdated",
            workItem,
            workItem.Status,
            correlationId,
            $"updated:{workItem.Version}",
            ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> SetCustomFieldsAsync(
        string id,
        SetWorkItemCustomFieldsRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        var shape = await typeSchemas.ValidateAsync(workItem.ProjectId, workItem.Type, request.Values, ct);
        var oldValue = string.Join('|', workItem.CustomFields.Select(value => $"{value.FieldKey}:{value.SearchValue}"));
        workItem.Type = shape.IssueTypeKey;
        workItem.IssueTypeSchemaVersion = shape.SchemaVersion;
        workItem.CustomFields = shape.CustomFields.ToList();
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync(
            "WorkItemCustomFieldsUpdated",
            "WorkItem",
            workItem.Id,
            oldValue,
            string.Join('|', workItem.CustomFields.Select(value => $"{value.FieldKey}:{value.SearchValue}")),
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemCustomFieldsUpdated", "Custom fields updated", correlationId, ct);
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        await PublishAutomationAsync(
            "WorkItemUpdated",
            workItem,
            workItem.Status,
            correlationId,
            $"assigned:{workItem.Version}",
            ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> ClearAssigneeAsync(
        string id,
        string correlationId,
        CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemAssign, ct);
        if (workItem.AssigneeUserId is null)
            return ToResponse(workItem);

        var oldAssignee = workItem.AssigneeUserId;
        workItem.AssigneeUserId = null;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync(
            "WorkItemAssigneeCleared",
            "WorkItem",
            workItem.Id,
            oldAssignee,
            null,
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemAssigneeCleared",
            "Assignee cleared",
            correlationId,
            ct);
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        await PublishAutomationAsync(
            "WorkItemUpdated",
            workItem,
            workItem.Status,
            correlationId,
            $"assignee-cleared:{workItem.Version}",
            ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> AssignAsync(string id, AssignWorkItemRequest request, string correlationId, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemAssign", ct);
        if (workItem.TeamId is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(workItem.ProjectId, workItem.TeamId, request.AssigneeUserId, ct);
        }
        var oldAssignee = workItem.AssigneeUserId;
        workItem.AssigneeUserId = request.AssigneeUserId;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemAssigned", "WorkItem", workItem.Id, oldAssignee, request.AssigneeUserId, correlationId, ct);
        await notifications.NotifyAsync(request.AssigneeUserId, "Assignment", $"Assigned to {workItem.Title}", ct);
        if (collaborationService is not null)
        {
            var organizationId = CurrentOrganizationId(workItem.ProjectId);
            await collaborationService.RecordActivityAsync(
                workItem, organizationId, "WorkItemAssigned", "Assignee changed", correlationId, ct);
            await collaborationService.NotifyWatchersAsync(
                workItem,
                organizationId,
                "WatcherAssignment",
                $"The assignee changed on {workItem.Title}",
                correlationId,
                [request.AssigneeUserId],
                ct);
        }
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> SetTeamAsync(
        string id,
        SetWorkItemTeamRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        var teamId = NormalizeOptionalId(request.TeamId);
        if (teamId is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(workItem.ProjectId, teamId, workItem.AssigneeUserId, ct);
        }

        if (workItem.TeamId == teamId)
        {
            throw new ConflictException("WORK_ITEM_TEAM_UNCHANGED", "Work item already has the requested team.");
        }

        var oldTeamId = workItem.TeamId;
        workItem.TeamId = teamId;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync("WorkItemTeamChanged", "WorkItem", workItem.Id, oldTeamId, teamId, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemTeamChanged", "Team changed", correlationId, ct);
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> RequestApprovalAsync(
        string id,
        RequestWorkItemApprovalRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemMove", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var target = request.TargetStatus?.Trim();
        if (string.IsNullOrWhiteSpace(target) || workItem.Status.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Approval target status must differ from the current status.");
        }

        var rule = await workflowPolicy.EnsureTransitionAllowedAsync(workItem.ProjectId, workItem.Type, workItem.Status, target, ct);
        if (!rule.RequiresApproval)
        {
            throw new ConflictException("WORK_ITEM_APPROVAL_NOT_REQUIRED", "The requested transition does not require approval.");
        }

        var now = clock.UtcNow;
        if (workItem.Approvals.Any(x =>
            x.FromStatus.Equals(workItem.Status, StringComparison.OrdinalIgnoreCase)
            && x.ToStatus.Equals(target, StringComparison.OrdinalIgnoreCase)
            && x.ConsumedAt is null
            && x.ExpiresAt > now
            && x.Status is "Pending" or "Approved"))
        {
            throw new ConflictException("WORK_ITEM_APPROVAL_EXISTS", "An active approval already exists for this transition.");
        }

        var approval = new WorkItemApprovalDocument
        {
            FromStatus = workItem.Status,
            ToStatus = rule.ToStatus,
            RequestedByUserId = currentUser.UserId ?? "system",
            RequestedAt = now,
            ExpiresAt = now.AddDays(7)
        };
        workItem.Approvals.Add(approval);
        workItem.UpdatedAt = now;
        await SaveAsync(workItem, ct);
        await activityStore.CreateApprovalAsync(
            WorkItemActivityStore.ToActivity(workItem, CurrentOrganizationId(workItem.ProjectId), approval),
            ct);
        await audit.WriteAsync("WorkItemApprovalRequested", "WorkItem", workItem.Id, null, approval.Id, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemApprovalRequested", "Approval requested", correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> DecideApprovalAsync(
        string id,
        string approvalId,
        DecideWorkItemApprovalRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemApprove", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var approval = workItem.Approvals.SingleOrDefault(x => x.Id == approvalId)
            ?? throw new NotFoundException("WORK_ITEM_APPROVAL_NOT_FOUND", "Work item approval was not found.");
        if (approval.Status != "Pending")
        {
            throw new ConflictException("WORK_ITEM_APPROVAL_DECIDED", "Work item approval has already been decided.");
        }

        var now = clock.UtcNow;
        if (approval.ExpiresAt <= now)
        {
            approval.Status = "Expired";
            workItem.UpdatedAt = now;
            await SaveAsync(workItem, ct);
            await UpdateApprovalActivityAsync(workItem, approval, ct);
            await RecordActivityAndNotifyWatchersAsync(
                workItem, "WorkItemApprovalExpired", "Approval expired", correlationId, ct);
            throw new ConflictException("WORK_ITEM_APPROVAL_EXPIRED", "Work item approval has expired.");
        }

        var actorUserId = currentUser.UserId ?? "system";
        if (approval.RequestedByUserId == actorUserId)
        {
            throw new ForbiddenException("Approval requester cannot decide their own request.");
        }

        var note = request.Note?.Trim();
        if (note?.Length > 1000)
        {
            throw new ValidationException("Approval note cannot exceed 1000 characters.");
        }

        approval.Status = request.Approved ? "Approved" : "Rejected";
        approval.DecidedByUserId = actorUserId;
        approval.DecidedAt = now;
        approval.Note = string.IsNullOrWhiteSpace(note) ? null : note;
        workItem.UpdatedAt = now;
        await SaveAsync(workItem, ct);
        await UpdateApprovalActivityAsync(workItem, approval, ct);
        await audit.WriteAsync("WorkItemApprovalDecided", "WorkItem", workItem.Id, "Pending", approval.Status, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemApprovalDecided", $"Approval {approval.Status.ToLowerInvariant()}", correlationId, ct);
        await notifications.NotifyAsync(
            approval.RequestedByUserId,
            "Approval",
            $"Approval for {workItem.Title} was {approval.Status.ToLowerInvariant()}.",
            ct);
        return ToResponse(workItem);
    }
}
