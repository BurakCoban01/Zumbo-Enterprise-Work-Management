using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;

public sealed class IdentityAuditWriterAdapter(AuditService audit) : IIdentityAuditWriter
{
    public Task WriteAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(action, "Identity", entityId, oldValue, newValue, correlationId, ct);
}
