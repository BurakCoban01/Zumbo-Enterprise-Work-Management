namespace Zumbo.Modules.Projects;

public interface IProjectAuditWriter
{
    Task WriteAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}
