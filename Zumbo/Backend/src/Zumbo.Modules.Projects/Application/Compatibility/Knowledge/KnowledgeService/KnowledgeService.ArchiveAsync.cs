using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    public async Task ArchiveAsync(
        string documentId,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var document = await GetDocumentAsync(documentId, includeArchived: false, ct);
        var access = await AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        EnsureCanEdit(document, access, actor.UserId);
        document.Archived = true;
        document.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(document, ct);
        await audit.WriteAsync(
            "KnowledgeDocumentArchived",
            document.Id,
            "Active",
            "Archived",
            correlationId,
            ct);
    }
}
