using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

public sealed class QueryAuditLogHandler(AuditService service)
{
    private QueryAuditLogSlice? slice;

    public QueryAuditLogHandler(
        IDocumentRepository<AuditLogDocument> auditLogs,
        IAuditAccessChecker accessChecker)
        : this(null!)
    {
        slice = new QueryAuditLogSlice(auditLogs, accessChecker);
    }

    public Task<AuditLogPageResponse> HandleAsync(AuditLogQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.QueryAsync(query, ct);
}
