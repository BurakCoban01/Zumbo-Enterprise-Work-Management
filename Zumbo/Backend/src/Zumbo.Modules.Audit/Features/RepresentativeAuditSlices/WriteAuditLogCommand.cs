using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

public sealed record WriteAuditLogCommand(
    string Action,
    string EntityType,
    string EntityId,
    string? OldValue,
    string? NewValue,
    string CorrelationId);
