using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

public sealed record AuditChangeResponse(
    string Field,
    string? OldValue,
    string? NewValue,
    bool Redacted);
