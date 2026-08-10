using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed class AddKnowledgeVersionHandler(KnowledgeService service)
{
    private AddKnowledgeVersionSlice? slice;

    public AddKnowledgeVersionHandler(
        IDocumentRepository<KnowledgeDocument> documents,
        IKnowledgeDirectory directory,
        IKnowledgeAuditWriter audit,
        ICurrentUser currentUser,
        IClock clock,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(null!)
    {
        slice = new AddKnowledgeVersionSlice(
            documents,
            directory,
            audit,
            currentUser,
            clock,
            expectedVersions);
    }

    public Task<KnowledgeDocumentResponse> HandleAsync(
        AddKnowledgeVersionCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.AddVersionAsync(
            command.DocumentId,
            command.Request,
            command.CorrelationId,
            ct);
}
