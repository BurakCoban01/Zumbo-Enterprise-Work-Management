using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal sealed class ResolveKnowledgeCommentSlice(
    IDocumentRepository<KnowledgeDocument> documents,
    IKnowledgeDirectory directory,
    IKnowledgeAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions)
{
    private readonly KnowledgeReadAccess access = new(documents, directory, currentUser);
    private readonly KnowledgeMutationPersistence persistence = new(documents, expectedVersions);

    internal async Task<KnowledgeDocumentResponse> HandleAsync(
        ResolveKnowledgeCommentCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var document = await access.GetDocumentAsync(command.DocumentId, includeArchived: false, ct);
        var scopeAccess = await access.AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        var comment = document.Comments.SingleOrDefault(item => item.Id == command.CommentId)
            ?? throw new NotFoundException(
                "KNOWLEDGE_COMMENT_NOT_FOUND",
                "Knowledge comment was not found.");
        if (comment.AuthorUserId != actor.UserId
            && document.OwnerUserId != actor.UserId
            && !scopeAccess.CanManage)
        {
            throw new ForbiddenException(
                "Only the comment author or a document manager can resolve this comment.");
        }
        if (!comment.Resolved)
        {
            comment.Resolved = true;
            comment.ResolvedByUserId = actor.UserId;
            comment.ResolvedAt = clock.UtcNow;
            document.UpdatedAt = clock.UtcNow;
            await persistence.ReplaceAsync(document, ct);
            await audit.WriteAsync(
                "KnowledgeCommentResolved",
                document.Id,
                comment.Id,
                "Resolved",
                command.CorrelationId,
                ct);
        }
        return KnowledgeResponseMapper.ToDocument(document, scopeAccess, actor.UserId);
    }
}
