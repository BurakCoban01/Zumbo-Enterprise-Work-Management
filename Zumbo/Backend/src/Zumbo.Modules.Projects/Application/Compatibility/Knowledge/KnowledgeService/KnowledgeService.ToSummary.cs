using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private static KnowledgeDocumentSummaryResponse ToSummary(
        KnowledgeDocument document,
        KnowledgeScopeAccess access,
        string userId) => new(
            document.Id,
            document.ScopeType,
            document.ScopeId,
            access.ScopeName,
            document.OwnerUserId,
            document.Title,
            Excerpt(document.ContentMarkdown),
            document.Tags,
            document.CurrentContentVersion,
            document.OwnerUserId == userId || access.CanManage,
            document.Archived,
            document.UpdatedAt,
            document.Version);
}
