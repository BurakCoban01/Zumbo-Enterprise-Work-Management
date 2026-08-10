using Zumbo.Modules.Projects.Application.Features.Knowledge;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService
{
    public async Task<KnowledgeDocumentResponse> AddCommentAsync(
        string documentId,
        AddKnowledgeCommentRequest request,
        string correlationId,
        CancellationToken ct) =>
        await addKnowledgeCommentHandler.HandleAsync(
            new AddKnowledgeCommentCommand(documentId, request, correlationId),
            ct);

    public async Task<KnowledgeDocumentResponse> ResolveCommentAsync(
        string documentId,
        string commentId,
        string correlationId,
        CancellationToken ct) =>
        await resolveKnowledgeCommentHandler.HandleAsync(
            new ResolveKnowledgeCommentCommand(
                documentId,
                commentId,
                correlationId),
            ct);
}
