using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemActivityStore{

    internal static WorkItemCommentActivityDocument ToActivity(
        WorkItemDocument item, string organizationId, CommentDocument source) => new()
    {
        Id = source.Id,
        OrganizationId = organizationId,
        ProjectId = item.ProjectId,
        WorkItemId = item.Id,
        Body = source.Body,
        AuthorUserId = source.AuthorUserId,
        Mentions = [.. source.Mentions],
        CreatedAt = source.CreatedAt,
        EditedAt = source.EditedAt
    };

    internal static WorkItemCommentRevisionActivityDocument ToActivity(
        WorkItemDocument item, string organizationId, string commentId, CommentRevisionDocument source, int ordinal) => new()
    {
        Id = DeterministicId("revision", item.Id, commentId, ordinal.ToString(), source.EditedAt.ToUniversalTime().Ticks.ToString()),
        OrganizationId = organizationId,
        ProjectId = item.ProjectId,
        WorkItemId = item.Id,
        CommentId = commentId,
        Body = source.Body,
        EditedByUserId = source.EditedByUserId,
        EditedAt = source.EditedAt
    };

    internal static WorkItemAttachmentActivityDocument ToActivity(
        WorkItemDocument item, string organizationId, AttachmentDocument source) => new()
    {
        Id = source.Id,
        OrganizationId = organizationId,
        ProjectId = item.ProjectId,
        WorkItemId = item.Id,
        FileName = source.FileName,
        ContentType = source.ContentType,
        SizeBytes = source.SizeBytes,
            StoragePath = source.StoragePath,
            ChecksumSha256 = source.ChecksumSha256,
            SecurityState = source.SecurityState,
            ScanProvider = source.ScanProvider,
            ScanDetail = source.ScanDetail,
            ScannedAt = source.ScannedAt,
            CreatedAt = source.CreatedAt
    };

    internal static WorkItemWorkLogActivityDocument ToActivity(
        WorkItemDocument item, string organizationId, WorkLogDocument source) => new()
    {
        Id = source.Id,
        OrganizationId = organizationId,
        ProjectId = item.ProjectId,
        WorkItemId = item.Id,
        UserId = source.UserId,
        Hours = source.Hours,
        Note = source.Note,
        CreatedAt = source.CreatedAt
    };

    internal static WorkItemApprovalActivityDocument ToActivity(
        WorkItemDocument item, string organizationId, WorkItemApprovalDocument source) => new()
    {
        Id = source.Id,
        OrganizationId = organizationId,
        ProjectId = item.ProjectId,
        WorkItemId = item.Id,
        FromStatus = source.FromStatus,
        ToStatus = source.ToStatus,
        RequestedByUserId = source.RequestedByUserId,
        RequestedAt = source.RequestedAt,
        ExpiresAt = source.ExpiresAt,
        Status = source.Status,
        DecidedByUserId = source.DecidedByUserId,
        DecidedAt = source.DecidedAt,
        Note = source.Note,
        ConsumedAt = source.ConsumedAt
    };

    internal static WorkItemTimelineActivityDocument ToActivity(
        WorkItemDocument item, string organizationId, WorkItemStatusHistoryDocument source, int ordinal) => new()
    {
        Id = DeterministicId("timeline", item.Id, ordinal.ToString(), source.ChangedAt.ToUniversalTime().Ticks.ToString(), source.ToStatus),
        OrganizationId = organizationId,
        ProjectId = item.ProjectId,
        WorkItemId = item.Id,
        FromStatus = source.FromStatus,
        ToStatus = source.ToStatus,
        ChangedByUserId = source.ChangedByUserId,
        ChangedAt = source.ChangedAt
    };
}
