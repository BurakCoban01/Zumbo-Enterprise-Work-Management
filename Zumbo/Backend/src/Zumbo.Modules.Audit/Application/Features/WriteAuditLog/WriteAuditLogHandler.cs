using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

public sealed class WriteAuditLogHandler(AuditService service)
{
    private WriteAuditLogSlice? slice;

    public WriteAuditLogHandler(
        IDocumentRepository<AuditLogDocument> auditLogs,
        IClock clock,
        ICurrentUser currentUser,
        IAuditRequestContext requestContext,
        IOptions<AuditOptions> options,
        IAuditTenantResolver? tenantResolver,
        IDistributedLockProvider? distributedLocks)
        : this(null!)
    {
        slice = new WriteAuditLogSlice(
            auditLogs,
            clock,
            currentUser,
            requestContext,
            options,
            tenantResolver,
            distributedLocks);
    }

    public async Task<WriteAuditLogResponse> HandleAsync(WriteAuditLogCommand command, CancellationToken ct)
    {
        WriteAuditLogValidator.Validate(command);
        await HandleUncheckedAsync(command, ct);
        return new WriteAuditLogResponse(true);
    }

    internal Task HandleUncheckedAsync(WriteAuditLogCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.WriteAsync(
            command.Action,
            command.EntityType,
            command.EntityId,
            command.OldValue,
            command.NewValue,
            command.CorrelationId,
            ct);
}
