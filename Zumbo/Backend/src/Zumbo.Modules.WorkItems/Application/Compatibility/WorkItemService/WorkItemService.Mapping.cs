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
    private static WorkItemResponse ToResponse(WorkItemDocument item) =>
        new(
            item.Id,
            item.ProjectId,
            item.BoardId,
            item.ParentId,
            item.TeamId,
            item.ColumnId,
            item.Title,
            item.Description,
            item.Type,
            item.Priority,
            item.Status,
            item.AssigneeUserId,
            item.DueDate,
            item.SprintId,
            item.EstimatePoints,
            item.CompletedAt,
            item.StatusHistory
                .OrderBy(x => x.ChangedAt)
                .Select(x => new WorkItemStatusHistoryResponse(x.FromStatus, x.ToStatus, x.ChangedByUserId, x.ChangedAt))
                .ToList(),
            item.Labels,
            item.Checklist.Select(x => new ChecklistItemResponse(x.Id, x.Text, x.Completed)).ToList(),
            item.Comments.Select(x => new CommentResponse(
                x.Id,
                x.Body,
                x.AuthorUserId,
                x.Mentions,
                x.CreatedAt,
                x.EditedAt,
                x.History
                    .OrderBy(revision => revision.EditedAt)
                    .Select(revision => new CommentRevisionResponse(
                        revision.Body,
                        revision.EditedByUserId,
                        revision.EditedAt))
                    .ToList())).ToList(),
            item.Attachments.Select(x => new AttachmentResponse(
                x.Id, x.FileName, x.ContentType, x.SizeBytes, x.CreatedAt,
                x.SecurityState, x.ScanProvider, x.ScannedAt)).ToList(),
            item.WorkLogs.Select(x => new WorkLogResponse(x.Id, x.UserId, x.Hours, x.Note, x.CreatedAt)).ToList(),
            item.Relations.Select(x => new WorkItemRelationResponse(x.RelatedWorkItemId, x.RelationType)).ToList(),
            item.Approvals
                .OrderBy(x => x.RequestedAt)
                .Select(x => new WorkItemApprovalResponse(
                    x.Id,
                    x.FromStatus,
                    x.ToStatus,
                    x.RequestedByUserId,
                    x.RequestedAt,
                    x.ExpiresAt,
                    x.Status,
                    x.DecidedByUserId,
                    x.DecidedAt,
                    x.Note,
                    x.ConsumedAt))
                .ToList(),
            item.Rank,
            item.Archived,
            item.Version,
            item.IssueTypeSchemaVersion,
            item.CustomFields.Select(value => new WorkItemCustomFieldValueResponse(
                value.FieldKey,
                value.Type,
                value.TextValue,
                value.NumberValue,
                value.BooleanValue,
                value.DateValueUtc is null ? null : DateOnly.FromDateTime(value.DateValueUtc.Value.UtcDateTime),
                value.OptionKey)).ToList());

    internal static WorkItemRealtimeItem ToRealtimeItem(WorkItemDocument item) =>
        WorkItemPublicationMapper.ToRealtimeItem(item);
}
