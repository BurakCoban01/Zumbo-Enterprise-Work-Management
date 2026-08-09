using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed class AddKnowledgeCommentHandler(KnowledgeService service)
{
    private AddKnowledgeCommentSlice? slice;

    public AddKnowledgeCommentHandler(
        IDocumentRepository<KnowledgeDocument> documents,
        IKnowledgeDirectory directory,
        IKnowledgeAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(null!)
    {
        slice = new AddKnowledgeCommentSlice(
            documents,
            directory,
            audit,
            currentUser,
            clock,
            expectedVersions);
    }

    public Task<KnowledgeDocumentResponse> HandleAsync(
        AddKnowledgeCommentCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.AddCommentAsync(
            command.DocumentId,
            command.Request,
            command.CorrelationId,
            ct);
}
