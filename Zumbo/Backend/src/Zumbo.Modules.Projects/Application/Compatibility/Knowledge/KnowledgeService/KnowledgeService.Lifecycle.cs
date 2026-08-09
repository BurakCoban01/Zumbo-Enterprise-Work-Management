using Zumbo.Modules.Projects.Application.Features.Knowledge;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService
{
    public async Task<KnowledgeDocumentResponse> CreateAsync(
        CreateKnowledgeDocumentRequest request,
        string correlationId,
        CancellationToken ct) =>
        await createKnowledgeDocumentHandler.HandleAsync(
            new CreateKnowledgeDocumentCommand(request, correlationId),
            ct);

    public async Task<KnowledgeDocumentResponse> AddVersionAsync(
        string documentId,
        CreateKnowledgeVersionRequest request,
        string correlationId,
        CancellationToken ct) =>
        await addKnowledgeVersionHandler.HandleAsync(
            new AddKnowledgeVersionCommand(documentId, request, correlationId),
            ct);

    public async Task ArchiveAsync(
        string documentId,
        string correlationId,
        CancellationToken ct) =>
        await archiveKnowledgeDocumentHandler.HandleAsync(
            new ArchiveKnowledgeDocumentCommand(documentId, correlationId),
            ct);
}
