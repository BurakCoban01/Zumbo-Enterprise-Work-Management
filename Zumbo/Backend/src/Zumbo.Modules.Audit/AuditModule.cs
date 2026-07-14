using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

public sealed record AuditLogResponse(
    string Id,
    string ActorUserId,
    string Action,
    string EntityType,
    string EntityId,
    string? OldValue,
    string? NewValue,
    string? IpAddress,
    string? UserAgent,
    string CorrelationId,
    DateTimeOffset CreatedAt);

public sealed record AuditLogQuery(
    string? ActorUserId,
    string? Action,
    string? EntityType,
    string? EntityId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditLogPageResponse(
    IReadOnlyList<AuditLogResponse> Items,
    int Page,
    int PageSize,
    bool HasNextPage);

public interface IAuditAccessChecker
{
    Task EnsureCanReadAsync(AuditLogQuery query, CancellationToken ct);
}

public sealed record AuditRequestMetadata(string? IpAddress, string? UserAgent);

public interface IAuditRequestContext
{
    AuditRequestMetadata GetMetadata();
}

public sealed class AuditLogDocument : IDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ActorUserId { get; set; } = "system";
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditService(
    IDocumentRepository<AuditLogDocument> auditLogs,
    IClock clock,
    ICurrentUser currentUser,
    IAuditRequestContext requestContext,
    IAuditAccessChecker accessChecker)
{
    public async Task WriteAsync(
        string action,
        string entityType,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct)
    {
        var metadata = requestContext.GetMetadata();
        await auditLogs.CreateAsync(new AuditLogDocument
        {
            ActorUserId = currentUser.UserId ?? "system",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValue = oldValue,
            NewValue = newValue,
            IpAddress = NormalizeBounded(metadata.IpAddress, 64),
            UserAgent = NormalizeBounded(metadata.UserAgent, 512),
            CorrelationId = correlationId,
            CreatedAt = clock.UtcNow
        }, ct);
    }

    public async Task<IReadOnlyList<AuditLogResponse>> ListByEntityAsync(string entityType, string entityId, CancellationToken ct)
    {
        var result = await QueryAsync(new AuditLogQuery(null, null, entityType, entityId, null, null, 1, 100), ct);
        return result.Items;
    }

    public async Task<IReadOnlyList<AuditLogResponse>> ListByUserAsync(string actorUserId, CancellationToken ct)
    {
        var result = await QueryAsync(new AuditLogQuery(actorUserId, null, null, null, null, null, 1, 100), ct);
        return result.Items;
    }

    public async Task<AuditLogPageResponse> QueryAsync(AuditLogQuery query, CancellationToken ct)
    {
        var normalized = NormalizeAndValidate(query);
        await accessChecker.EnsureCanReadAsync(normalized, ct);
        var filter = BuildFilter(normalized);

        var result = await auditLogs.ListByFilterAsync(
            filter,
            x => x.CreatedAt,
            orderDescending: true,
            page: normalized.Page,
            pageSize: normalized.PageSize,
            cancellationToken: ct);

        var hasNextPage = false;
        if (result.Count == normalized.PageSize)
        {
            var nextPage = await auditLogs.ListByFilterAsync(
                filter,
                x => x.CreatedAt,
                orderDescending: true,
                page: normalized.Page + 1,
                pageSize: normalized.PageSize,
                cancellationToken: ct);
            hasNextPage = nextPage.Count > 0;
        }

        var items = result.Select(ToResponse).ToList();
        return new AuditLogPageResponse(items, normalized.Page, normalized.PageSize, hasNextPage);
    }

    private static Expression<Func<AuditLogDocument, bool>> BuildFilter(AuditLogQuery query) =>
        x => (query.ActorUserId == null || x.ActorUserId == query.ActorUserId)
            && (query.Action == null || x.Action == query.Action)
            && (query.EntityType == null || x.EntityType == query.EntityType)
            && (query.EntityId == null || x.EntityId == query.EntityId)
            && (query.From == null || x.CreatedAt >= query.From)
            && (query.To == null || x.CreatedAt <= query.To);

    private static AuditLogQuery NormalizeAndValidate(AuditLogQuery query)
    {
        var actorUserId = Normalize(query.ActorUserId);
        var action = Normalize(query.Action);
        var entityType = Normalize(query.EntityType);
        var entityId = Normalize(query.EntityId);

        if ((entityType is null) != (entityId is null))
        {
            throw new ValidationException("Entity type and entity id must be provided together.");
        }

        if (query.Page < 1)
        {
            throw new ValidationException("Audit page must be at least 1.");
        }

        if (query.PageSize is < 1 or > 100)
        {
            throw new ValidationException("Audit page size must be between 1 and 100.");
        }

        if (query.From.HasValue && query.To.HasValue)
        {
            if (query.To < query.From)
            {
                throw new ValidationException("Audit end date must be after start date.");
            }

            if (query.To.Value - query.From.Value > TimeSpan.FromDays(366))
            {
                throw new ValidationException("Audit date range cannot exceed 366 days.");
            }
        }

        return query with
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId
        };
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeBounded(string? value, int maximumLength)
    {
        var normalized = Normalize(value);
        return normalized?.Length > maximumLength ? normalized[..maximumLength] : normalized;
    }

    private static AuditLogResponse ToResponse(AuditLogDocument log) =>
        new(
            log.Id,
            log.ActorUserId,
            log.Action,
            log.EntityType,
            log.EntityId,
            log.OldValue,
            log.NewValue,
            log.IpAddress,
            log.UserAgent,
            log.CorrelationId,
            log.CreatedAt);
}
