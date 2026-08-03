using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class KnowledgeAuditWriterAdapter(AuditService audit) : IKnowledgeAuditWriter
{
    public Task WriteAsync(
        string action,
        string documentId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(
            action,
            "KnowledgeDocument",
            documentId,
            oldValue,
            newValue,
            correlationId,
            ct);
}
