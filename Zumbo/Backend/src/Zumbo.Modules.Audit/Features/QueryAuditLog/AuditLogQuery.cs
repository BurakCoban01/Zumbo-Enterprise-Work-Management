using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

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
