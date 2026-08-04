using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    public async Task<KnowledgeDocumentResponse> ResolveCommentAsync(
        string documentId,
        string commentId,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var document = await GetDocumentAsync(documentId, includeArchived: false, ct);
        var access = await AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        var comment = document.Comments.SingleOrDefault(item => item.Id == commentId)
            ?? throw new NotFoundException(
                "KNOWLEDGE_COMMENT_NOT_FOUND",
                "Knowledge comment was not found.");
        if (comment.AuthorUserId != actor.UserId
            && document.OwnerUserId != actor.UserId
            && !access.CanManage)
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
            await ReplaceAsync(document, ct);
            await audit.WriteAsync(
                "KnowledgeCommentResolved",
                document.Id,
                comment.Id,
                "Resolved",
                correlationId,
                ct);
        }
        return ToResponse(document, access, actor.UserId);
    }
}
