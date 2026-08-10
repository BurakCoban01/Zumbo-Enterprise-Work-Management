using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal sealed class AddKnowledgeVersionSlice(
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
        AddKnowledgeVersionCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var document = await access.GetDocumentAsync(command.DocumentId, includeArchived: false, ct);
        var scopeAccess = await access.AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        KnowledgeVersionPolicy.EnsureCanEdit(document, scopeAccess, actor.UserId);
        if (document.Versions.Count >= KnowledgeLimits.MaximumVersions)
        {
            throw new ValidationException(
                $"A knowledge document cannot contain more than {KnowledgeLimits.MaximumVersions} versions.");
        }

        var version = KnowledgeVersionPolicy.Normalize(
            command.Request.Title,
            command.Request.ContentMarkdown,
            command.Request.Tags,
            command.Request.WorkItemIds,
            command.Request.UserIds,
            command.Request.ChangeSummary,
            actor.UserId,
            document.CurrentContentVersion + 1,
            clock.UtcNow);
        await directory.EnsureLinksAsync(
            document.OrganizationId,
            scopeAccess.ProjectIds,
            version.WorkItemIds,
            version.UserIds,
            ct);

        var oldTitle = document.Title;
        KnowledgeVersionPolicy.Apply(document, version);
        document.Versions.Add(version);
        document.UpdatedAt = clock.UtcNow;
        await persistence.ReplaceAsync(document, ct);
        await audit.WriteAsync(
            "KnowledgeDocumentVersionCreated",
            document.Id,
            oldTitle,
            document.Title,
            command.CorrelationId,
            ct);
        return KnowledgeResponseMapper.ToDocument(document, scopeAccess, actor.UserId);
    }
}
