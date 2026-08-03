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
    DateTimeOffset CreatedAt,
    string OrganizationId = "",
    string SubjectType = "",
    string SubjectId = "",
    IReadOnlyList<AuditChangeResponse>? Changes = null,
    long ChainSequence = 0,
    string? PreviousHash = null,
    string? RecordHash = null);
