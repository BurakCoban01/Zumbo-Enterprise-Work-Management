using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    public async Task<KnowledgeDocumentResponse> AddVersionAsync(
        string documentId,
        CreateKnowledgeVersionRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var document = await GetDocumentAsync(documentId, includeArchived: false, ct);
        var access = await AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        EnsureCanEdit(document, access, actor.UserId);
        if (document.Versions.Count >= KnowledgeLimits.MaximumVersions)
        {
            throw new ValidationException(
                $"A knowledge document cannot contain more than {KnowledgeLimits.MaximumVersions} versions.");
        }

        var version = NormalizeVersion(
            request.Title,
            request.ContentMarkdown,
            request.Tags,
            request.WorkItemIds,
            request.UserIds,
            request.ChangeSummary,
            actor.UserId,
            document.CurrentContentVersion + 1,
            clock.UtcNow);
        await directory.EnsureLinksAsync(
            document.OrganizationId,
            access.ProjectIds,
            version.WorkItemIds,
            version.UserIds,
            ct);

        var oldTitle = document.Title;
        Apply(document, version);
        document.Versions.Add(version);
        document.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(document, ct);
        await audit.WriteAsync(
            "KnowledgeDocumentVersionCreated",
            document.Id,
            oldTitle,
            document.Title,
            correlationId,
            ct);
        return ToResponse(document, access, actor.UserId);
    }
}
