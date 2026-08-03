using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

public sealed record AuditLogPageResponse(
    IReadOnlyList<AuditLogResponse> Items,
    int Page,
    int PageSize,
    bool HasNextPage,
    string? NextCursor = null);
