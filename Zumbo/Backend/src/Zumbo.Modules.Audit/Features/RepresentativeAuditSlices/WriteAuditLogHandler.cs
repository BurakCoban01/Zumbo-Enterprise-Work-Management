using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

public sealed class WriteAuditLogHandler(AuditService service)
{
    public async Task<WriteAuditLogResponse> HandleAsync(WriteAuditLogCommand command, CancellationToken ct)
    {
        WriteAuditLogValidator.Validate(command);
        await service.WriteAsync(
            command.Action,
            command.EntityType,
            command.EntityId,
            command.OldValue,
            command.NewValue,
            command.CorrelationId,
            ct);
        return new WriteAuditLogResponse(true);
    }
}
