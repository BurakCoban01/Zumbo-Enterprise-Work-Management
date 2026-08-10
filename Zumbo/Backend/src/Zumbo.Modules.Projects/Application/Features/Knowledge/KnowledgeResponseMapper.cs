using System.Text.RegularExpressions;
using Zumbo.Modules.Projects;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal static partial class KnowledgeResponseMapper
{
    internal static KnowledgeDocumentResponse ToDocument(
        KnowledgeDocument document,
        KnowledgeScopeAccess access,
        string userId) => new(
            document.Id,
            document.ScopeType,
            document.ScopeId,
            access.ScopeName,
            document.OwnerUserId,
            document.Title,
            document.ContentMarkdown,
            document.Tags,
            document.WorkItemIds,
            document.UserIds,
            document.CurrentContentVersion,
            document.Versions
                .OrderByDescending(item => item.Number)
                .Select(item => new KnowledgeVersionSummaryResponse(
                    item.Number,
                    item.Title,
                    item.ChangeSummary,
                    item.AuthorUserId,
                    item.CreatedAt))
                .ToList(),
            document.Comments
                .OrderByDescending(item => item.CreatedAt)
                .Select(ToComment)
                .ToList(),
            document.OwnerUserId == userId || access.CanManage,
            access.CanComment,
            document.Archived,
            document.UpdatedAt,
            document.Version);

    internal static KnowledgeVersionResponse ToVersion(KnowledgeVersionDocument version) => new(
        version.Number,
        version.Title,
        version.ContentMarkdown,
        version.Tags,
        version.WorkItemIds,
        version.UserIds,
        version.ChangeSummary,
        version.AuthorUserId,
        version.CreatedAt);

    internal static KnowledgeDocumentSummaryResponse ToSummary(
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

    private static KnowledgeCommentResponse ToComment(KnowledgeCommentDocument comment) => new(
        comment.Id,
        comment.Body,
        comment.AuthorUserId,
        comment.Resolved,
        comment.ResolvedByUserId,
        comment.ResolvedAt,
        comment.CreatedAt);

    private static string Excerpt(string value)
    {
        var compact = WhitespacePattern().Replace(value, " ").Trim();
        return compact.Length <= 220 ? compact : compact[..217] + "...";
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
