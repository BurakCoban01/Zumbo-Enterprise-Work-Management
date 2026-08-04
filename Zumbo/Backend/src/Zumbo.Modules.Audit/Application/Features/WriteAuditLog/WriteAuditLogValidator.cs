using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

public sealed class WriteAuditLogValidator
{
    public static void Validate(WriteAuditLogCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Action)
            || string.IsNullOrWhiteSpace(command.EntityType)
            || string.IsNullOrWhiteSpace(command.EntityId)
            || string.IsNullOrWhiteSpace(command.CorrelationId))
            throw new ValidationException("Audit action, subject and correlation id are required.");
    }
}
