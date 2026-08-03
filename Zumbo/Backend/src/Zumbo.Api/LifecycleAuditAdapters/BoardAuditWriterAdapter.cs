using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;

public sealed class BoardAuditWriterAdapter(WriteAuditLogHandler handler) : IBoardAuditWriter
{
    public async Task WriteAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct)
    {
        await handler.HandleAsync(
            new WriteAuditLogCommand(action, "Board", entityId, oldValue, newValue, correlationId),
            ct);
    }
}
