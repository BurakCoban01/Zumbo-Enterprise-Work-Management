using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

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
