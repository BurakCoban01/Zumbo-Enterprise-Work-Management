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

public interface IWorkItemActivityStore
{
    Task<bool> MigrateEmbeddedAsync(WorkItemDocument workItem, string organizationId, CancellationToken ct);
    Task HydrateAsync(WorkItemDocument workItem, string organizationId, CancellationToken ct);

    Task CreateCommentAsync(WorkItemCommentActivityDocument comment, CancellationToken ct);
    Task<WorkItemCommentActivityDocument?> GetCommentAsync(
        string organizationId, string projectId, string workItemId, string commentId, CancellationToken ct);
    Task UpdateCommentAsync(WorkItemCommentActivityDocument comment, CancellationToken ct);
    Task DeleteCommentAsync(WorkItemCommentActivityDocument comment, CancellationToken ct);
    Task CreateRevisionAsync(WorkItemCommentRevisionActivityDocument revision, CancellationToken ct);
    Task CreateAttachmentAsync(WorkItemAttachmentActivityDocument attachment, CancellationToken ct);
    Task<WorkItemAttachmentActivityDocument?> GetAttachmentAsync(
        string organizationId, string projectId, string workItemId, string attachmentId, CancellationToken ct);
    Task DeleteAttachmentAsync(WorkItemAttachmentActivityDocument attachment, CancellationToken ct);
    Task CreateWorkLogAsync(WorkItemWorkLogActivityDocument workLog, CancellationToken ct);
    Task CreateApprovalAsync(WorkItemApprovalActivityDocument approval, CancellationToken ct);
    Task<WorkItemApprovalActivityDocument?> GetApprovalAsync(
        string organizationId, string projectId, string workItemId, string approvalId, CancellationToken ct);
    Task UpdateApprovalAsync(WorkItemApprovalActivityDocument approval, CancellationToken ct);
    Task CreateTimelineAsync(WorkItemTimelineActivityDocument timeline, CancellationToken ct);

    Task<WorkItemActivityPage<CommentResponse>> ListCommentsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct);
    Task<WorkItemActivityPage<CommentRevisionResponse>> ListRevisionsAsync(
        string organizationId, string projectId, string workItemId, string commentId, int page, int pageSize, CancellationToken ct);
    Task<WorkItemActivityPage<AttachmentResponse>> ListAttachmentsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct);
    Task<WorkItemActivityPage<WorkLogResponse>> ListWorkLogsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct);
    Task<WorkItemActivityPage<WorkItemApprovalResponse>> ListApprovalsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct);
    Task<WorkItemActivityPage<WorkItemStatusHistoryResponse>> ListTimelineAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyDictionary<string, WorkItemUserActivityReference>> FindUserReferencesAsync(
        string organizationId, string userId, CancellationToken ct);
    IAsyncEnumerable<WorkItemUserActivityReference> StreamUserReferencesAsync(
        string organizationId, string userId, CancellationToken ct);
    Task<WorkItemReportActivityData> ReadReportDataAsync(
        string organizationId, string projectId, CancellationToken ct);
    Task AnonymizeUserReferencesAsync(
        string organizationId, string userId, string pseudonym, CancellationToken ct);
}
