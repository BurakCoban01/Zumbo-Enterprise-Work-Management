using System.Linq.Expressions;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

internal sealed class QueryAuditLogSlice(
    IDocumentRepository<AuditLogDocument> auditLogs,
    IAuditAccessChecker accessChecker)
{
    internal async Task<AuditLogPageResponse> HandleAsync(AuditLogQuery query, CancellationToken ct)
    {
        var normalized = QueryAuditLogValidator.ValidateAndNormalize(query);
        var scope = await accessChecker.EnsureCanReadAsync(normalized, ct);
        var cursor = DecodeCursor(normalized.Cursor);
        var filter = BuildFilter(normalized, scope.OrganizationId, cursor);
        var requested = normalized.Cursor is null ? normalized.PageSize : normalized.PageSize + 1;
        var page = normalized.Cursor is null ? normalized.Page : 1;
        var result = await auditLogs.ListByFilterAsync(
            filter,
            x => x.CreatedAt,
            orderDescending: true,
            page,
            requested,
            ct);
        var hasNext = result.Count > normalized.PageSize;
        var items = result.Take(normalized.PageSize).ToList();
        if (normalized.Cursor is null && items.Count == normalized.PageSize)
        {
            hasNext = (await auditLogs.ListByFilterAsync(
                filter,
                x => x.CreatedAt,
                true,
                normalized.Page + 1,
                normalized.PageSize,
                ct)).Count > 0;
        }

        var nextCursor = hasNext && items.Count > 0 ? EncodeCursor(items[^1]) : null;
        return new AuditLogPageResponse(
            items.Select(AuditLogResponseMapper.ToResponse).ToList(),
            normalized.Page,
            normalized.PageSize,
            hasNext,
            nextCursor);
    }

    private static Expression<Func<AuditLogDocument, bool>> BuildFilter(
        AuditLogQuery query,
        string organizationId,
        AuditCursor? cursor) =>
        x => x.OrganizationId == organizationId
            && (query.ActorUserId == null || x.ActorUserId == query.ActorUserId)
            && (query.Action == null || x.Action == query.Action)
            && (query.EntityType == null || x.EntityType == query.EntityType)
            && (query.EntityId == null || x.EntityId == query.EntityId)
            && (query.From == null || x.CreatedAt >= query.From)
            && (query.To == null || x.CreatedAt <= query.To)
            && (cursor == null || x.CreatedAt < cursor.CreatedAt
                || (x.CreatedAt == cursor.CreatedAt && x.Id.CompareTo(cursor.Id) > 0));

    private static string EncodeCursor(AuditLogDocument document) => Convert.ToBase64String(
        Encoding.UTF8.GetBytes($"{document.CreatedAt.UtcTicks}|{document.Id}"));

    private static AuditCursor? DecodeCursor(string? cursor)
    {
        if (cursor is null)
        {
            return null;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|', 2);
            if (parts.Length != 2
                || string.IsNullOrWhiteSpace(parts[1])
                || !long.TryParse(parts[0], out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks
                || ticks > DateTimeOffset.MaxValue.UtcTicks)
            {
                throw new ValidationException("Audit cursor is invalid.");
            }

            return new AuditCursor(new DateTimeOffset(ticks, TimeSpan.Zero), parts[1]);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ValidationException("Audit cursor is invalid.");
        }
    }

    private sealed record AuditCursor(DateTimeOffset CreatedAt, string Id);
}
