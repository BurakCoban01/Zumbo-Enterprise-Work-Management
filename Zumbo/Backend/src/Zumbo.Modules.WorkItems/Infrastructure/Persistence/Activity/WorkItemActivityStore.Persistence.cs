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

public sealed partial class WorkItemActivityStore
{
    private static Expression<Func<TDocument, object>> BoxOrder<TDocument, TOrder>(
        Expression<Func<TDocument, TOrder>> expression)
    {
        var body = expression.Body.Type == typeof(object)
            ? expression.Body
            : Expression.Convert(expression.Body, typeof(object));
        return Expression.Lambda<Func<TDocument, object>>(body, expression.Parameters);
    }

    private static string DeterministicId(params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }

    private static async Task CreateOwnedAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        TDocument document,
        CancellationToken ct)
        where TDocument : class, IWorkItemActivityDocument
    {
        ValidateActivityOwnership(document);
        await repository.CreateAsync(document, ct);
    }

    private static async Task CreateOrValidateAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        TDocument document,
        Func<TDocument, bool> compatible,
        CancellationToken ct)
        where TDocument : class, IVersionedDocument
    {
        try
        {
            await repository.CreateAsync(document, ct);
        }
        catch (DocumentConflictException)
        {
            var existing = await repository.SelectAsync(x => x.Id == document.Id, ct);
            if (existing is null || !compatible(existing))
            {
                throw new ConflictException(
                    "WORK_ITEM_ACTIVITY_MIGRATION_CONFLICT",
                    "Legacy work item activity conflicts with an existing activity record.");
            }
        }
    }

    private static async Task<IReadOnlyList<TDocument>> LoadAllAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        System.Linq.Expressions.Expression<Func<TDocument, bool>> filter,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        var result = new List<TDocument>();
        string? cursor = null;
        do
        {
            var current = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            result.AddRange(current.Items);
            cursor = current.NextCursor;
        }
        while (cursor is not null);
        return result;
    }

    private static (int Page, int PageSize) NormalizePage(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, 100));

    private static async Task<WorkItemActivityPage<TResponse>> PageAsync<TDocument, TOrder, TResponse>(
        IDocumentRepository<TDocument> repository,
        System.Linq.Expressions.Expression<Func<TDocument, bool>> filter,
        System.Linq.Expressions.Expression<Func<TDocument, TOrder>> orderBy,
        Func<TDocument, TResponse> map,
        int page,
        int pageSize,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        var normalized = NormalizePage(page, pageSize);
        var items = await repository.ListByFilterAsync(
            filter,
            BoxOrder(orderBy),
            page: normalized.Page,
            pageSize: normalized.PageSize,
            cancellationToken: ct);
        return new(items.Select(map).ToList(), normalized.Page, normalized.PageSize,
            await repository.CountByFilterAsync(filter, ct));
    }

    private static async Task ReplaceOwnedAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        TDocument document,
        CancellationToken ct)
        where TDocument : class, IWorkItemActivityDocument
    {
        ValidateActivityOwnership(document);
        var result = await repository.ReplaceByVersionAsync(x => x.Id == document.Id, document, document.Version, ct);
        if (!result.Found)
        {
            throw new NotFoundException("WORK_ITEM_ACTIVITY_NOT_FOUND", "Work item activity was not found.");
        }
        document.Version = result.Version!.Value;
    }

    private static bool SameOwner(
        WorkItemCommentActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemCommentRevisionActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemAttachmentActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemWorkLogActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemApprovalActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemTimelineActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;

    private static bool SamePayload<TDocument>(TDocument stored, TDocument expected)
        where TDocument : class, IVersionedDocument
    {
        var storedNode = JsonSerializer.SerializeToNode(stored)?.AsObject();
        var expectedNode = JsonSerializer.SerializeToNode(expected)?.AsObject();
        storedNode?.Remove(nameof(IVersionedDocument.Version));
        expectedNode?.Remove(nameof(IVersionedDocument.Version));
        return JsonNode.DeepEquals(storedNode, expectedNode);
    }

    private static async IAsyncEnumerable<TDocument> StreamAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        Expression<Func<TDocument, bool>> filter,
        [EnumeratorCancellation] CancellationToken ct)
        where TDocument : class, IDocument
    {
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            foreach (var item in page.Items) yield return item;
            cursor = page.NextCursor;
        }
        while (cursor is not null);
    }

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

    private static AttachmentDocument ToEmbedded(WorkItemAttachmentActivityDocument source) => new()
    {
        Id = source.Id,
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

    private static WorkLogDocument ToEmbedded(WorkItemWorkLogActivityDocument source) => new()
    {
        Id = source.Id,
        UserId = source.UserId,
        Hours = source.Hours,
        Note = source.Note,
        CreatedAt = source.CreatedAt
    };

    private static WorkItemApprovalDocument ToEmbedded(WorkItemApprovalActivityDocument source) => new()
    {
        Id = source.Id,
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

    private static WorkItemStatusHistoryDocument ToEmbedded(WorkItemTimelineActivityDocument source) => new()
    {
        FromStatus = source.FromStatus,
        ToStatus = source.ToStatus,
        ChangedByUserId = source.ChangedByUserId,
        ChangedAt = source.ChangedAt
    };

    private static void ValidateActivityOwnership(IWorkItemActivityDocument document) =>
        ValidateOwnership(document.OrganizationId, document.ProjectId, document.WorkItemId);

    private static void ValidateOwnership(string? organizationId, string? projectId, string? workItemId)
    {
        if (string.IsNullOrWhiteSpace(organizationId)
            || string.IsNullOrWhiteSpace(projectId)
            || string.IsNullOrWhiteSpace(workItemId))
        {
            throw new InvalidOperationException("Work item activity tenant, project and work-item ownership are required.");
        }
    }
}
