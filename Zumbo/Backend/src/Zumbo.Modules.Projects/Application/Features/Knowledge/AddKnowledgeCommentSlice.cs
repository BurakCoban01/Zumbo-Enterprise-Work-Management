using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal sealed class AddKnowledgeCommentSlice(
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
        AddKnowledgeCommentCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var document = await access.GetDocumentAsync(command.DocumentId, includeArchived: false, ct);
        var scopeAccess = await access.AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        if (!scopeAccess.CanComment)
            throw new ForbiddenException("Knowledge comment access is required.");
        if (document.Comments.Count >= KnowledgeLimits.MaximumComments)
        {
            throw new ValidationException(
                $"A knowledge document cannot contain more than {KnowledgeLimits.MaximumComments} comments.");
        }

        var comment = new KnowledgeCommentDocument
        {
            Body = KnowledgeQueryInput.Required(command.Request.Body, "Knowledge comment", 2_000),
            AuthorUserId = actor.UserId,
            CreatedAt = clock.UtcNow
        };
        document.Comments.Add(comment);
        document.UpdatedAt = clock.UtcNow;
        await persistence.ReplaceAsync(document, ct);
        await audit.WriteAsync(
            "KnowledgeCommentCreated",
            document.Id,
            null,
            comment.Id,
            command.CorrelationId,
            ct);
        return KnowledgeResponseMapper.ToDocument(document, scopeAccess, actor.UserId);
    }
}
