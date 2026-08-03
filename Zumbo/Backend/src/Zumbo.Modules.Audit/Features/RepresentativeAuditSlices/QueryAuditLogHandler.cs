using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

public sealed class QueryAuditLogHandler(AuditService service)
{
    public Task<AuditLogPageResponse> HandleAsync(AuditLogQuery query, CancellationToken ct) =>
        service.QueryAsync(query, ct);
}
