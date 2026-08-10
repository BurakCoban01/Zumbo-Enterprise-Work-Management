using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed class ResolveKnowledgeCommentHandler(KnowledgeService service)
{
    private ResolveKnowledgeCommentSlice? slice;

    public ResolveKnowledgeCommentHandler(
        IDocumentRepository<KnowledgeDocument> documents,
        IKnowledgeDirectory directory,
        IKnowledgeAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(null!)
    {
        slice = new ResolveKnowledgeCommentSlice(
            documents,
            directory,
            audit,
            currentUser,
            clock,
            expectedVersions);
    }

    public Task<KnowledgeDocumentResponse> HandleAsync(
        ResolveKnowledgeCommentCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.ResolveCommentAsync(
            command.DocumentId,
            command.CommentId,
            command.CorrelationId,
            ct);
}
