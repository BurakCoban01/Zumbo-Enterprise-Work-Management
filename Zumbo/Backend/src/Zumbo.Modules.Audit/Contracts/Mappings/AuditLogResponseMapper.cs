namespace Zumbo.Modules.Audit;

internal static class AuditLogResponseMapper
{
    internal static AuditLogResponse ToResponse(AuditLogDocument log) => new(
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
        log.CreatedAt,
        log.OrganizationId,
        log.SubjectType,
        log.SubjectId,
        log.Changes.Select(x => new AuditChangeResponse(
            x.Field,
            x.OldValue,
            x.NewValue,
            x.Redacted)).ToList(),
        log.ChainSequence,
        log.PreviousHash,
        log.RecordHash);
}
