using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed class CreateKnowledgeDocumentHandler(KnowledgeService service)
{
    private CreateKnowledgeDocumentSlice? slice;

    public CreateKnowledgeDocumentHandler(
        IDocumentRepository<KnowledgeDocument> documents,
        IKnowledgeDirectory directory,
        IKnowledgeAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock)
        : this(null!)
    {
        slice = new CreateKnowledgeDocumentSlice(
            documents,
            directory,
            audit,
            currentUser,
            clock);
    }

    public Task<KnowledgeDocumentResponse> HandleAsync(
        CreateKnowledgeDocumentCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.CreateAsync(command.Request, command.CorrelationId, ct);
}
