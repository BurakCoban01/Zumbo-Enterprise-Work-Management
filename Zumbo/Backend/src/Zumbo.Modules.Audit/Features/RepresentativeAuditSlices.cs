using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

public sealed record AuditChangeResponse(
    string Field,
    string? OldValue,
    string? NewValue,
    bool Redacted);

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
    DateTimeOffset CreatedAt,
    string OrganizationId = "",
    string SubjectType = "",
    string SubjectId = "",
    IReadOnlyList<AuditChangeResponse>? Changes = null,
    long ChainSequence = 0,
    string? PreviousHash = null,
    string? RecordHash = null);

public sealed record AuditLogQuery(
    string? ActorUserId,
    string? Action,
    string? EntityType,
    string? EntityId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page = 1,
    int PageSize = 50,
    string? Cursor = null,
    string? OrganizationId = null);

public sealed record AuditLogPageResponse(
    IReadOnlyList<AuditLogResponse> Items,
    int Page,
    int PageSize,
    bool HasNextPage,
    string? NextCursor = null);

public sealed record WriteAuditLogCommand(
    string Action,
    string EntityType,
    string EntityId,
    string? OldValue,
    string? NewValue,
    string CorrelationId);

public sealed record WriteAuditLogResponse(bool Written);
public sealed record AuditRetentionResult(string OrganizationId, DateTimeOffset Cutoff, int Deleted);
public sealed record AuditIntegrityResult(
    string OrganizationId,
    int Verified,
    bool Valid,
    string? BrokenRecordId,
    bool CompleteHistory = true,
    long FirstSequence = 0,
    string? AnchorHash = null);

public sealed class WriteAuditLogValidator
{
    public static void Validate(WriteAuditLogCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Action)
            || string.IsNullOrWhiteSpace(command.EntityType)
            || string.IsNullOrWhiteSpace(command.EntityId)
            || string.IsNullOrWhiteSpace(command.CorrelationId))
            throw new ValidationException("Audit action, subject and correlation id are required.");
    }
}

public sealed class WriteAuditLogHandler(AuditService service)
{
    public async Task<WriteAuditLogResponse> HandleAsync(WriteAuditLogCommand command, CancellationToken ct)
    {
        WriteAuditLogValidator.Validate(command);
        await service.WriteAsync(
            command.Action,
            command.EntityType,
            command.EntityId,
            command.OldValue,
            command.NewValue,
            command.CorrelationId,
            ct);
        return new WriteAuditLogResponse(true);
    }
}

public sealed class QueryAuditLogValidator
{
    public static AuditLogQuery ValidateAndNormalize(AuditLogQuery query)
    {
        var actorUserId = Normalize(query.ActorUserId);
        var action = Normalize(query.Action);
        var entityType = Normalize(query.EntityType);
        var entityId = Normalize(query.EntityId);
        if ((entityType is null) != (entityId is null))
            throw new ValidationException("Entity type and entity id must be provided together.");
        if (query.Page < 1) throw new ValidationException("Audit page must be at least 1.");
        if (query.PageSize is < 1 or > 100)
            throw new ValidationException("Audit page size must be between 1 and 100.");
        if (query.From.HasValue && query.To.HasValue)
        {
            if (query.To < query.From) throw new ValidationException("Audit end date must be after start date.");
            if (query.To.Value - query.From.Value > TimeSpan.FromDays(366))
                throw new ValidationException("Audit date range cannot exceed 366 days.");
        }
        if (!string.IsNullOrWhiteSpace(query.Cursor) && query.Page != 1)
            throw new ValidationException("Audit cursor queries must use page 1.");
        return query with
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Cursor = Normalize(query.Cursor),
            OrganizationId = Normalize(query.OrganizationId)
        };
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class QueryAuditLogHandler(AuditService service)
{
    public Task<AuditLogPageResponse> HandleAsync(AuditLogQuery query, CancellationToken ct) =>
        service.QueryAsync(query, ct);
}
