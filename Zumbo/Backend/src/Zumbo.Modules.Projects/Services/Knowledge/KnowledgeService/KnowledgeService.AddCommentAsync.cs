using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    public async Task<KnowledgeDocumentResponse> AddCommentAsync(
        string documentId,
        AddKnowledgeCommentRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var document = await GetDocumentAsync(documentId, includeArchived: false, ct);
        var access = await AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        if (!access.CanComment)
            throw new ForbiddenException("Knowledge comment access is required.");
        if (document.Comments.Count >= KnowledgeLimits.MaximumComments)
        {
            throw new ValidationException(
                $"A knowledge document cannot contain more than {KnowledgeLimits.MaximumComments} comments.");
        }

        var comment = new KnowledgeCommentDocument
        {
            Body = Required(request.Body, "Knowledge comment", 2_000),
            AuthorUserId = actor.UserId,
            CreatedAt = clock.UtcNow
        };
        document.Comments.Add(comment);
        document.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(document, ct);
        await audit.WriteAsync(
            "KnowledgeCommentCreated",
            document.Id,
            null,
            comment.Id,
            correlationId,
            ct);
        return ToResponse(document, access, actor.UserId);
    }
}
