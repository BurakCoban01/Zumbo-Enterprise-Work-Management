using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed class ArchiveKnowledgeDocumentHandler(KnowledgeService service)
{
    private ArchiveKnowledgeDocumentSlice? slice;

    public ArchiveKnowledgeDocumentHandler(
        IDocumentRepository<KnowledgeDocument> documents,
        IKnowledgeDirectory directory,
        IKnowledgeAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(null!)
    {
        slice = new ArchiveKnowledgeDocumentSlice(
            documents,
            directory,
            audit,
            currentUser,
            clock,
            expectedVersions);
    }

    public Task HandleAsync(
        ArchiveKnowledgeDocumentCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.ArchiveAsync(command.DocumentId, command.CorrelationId, ct);
}
