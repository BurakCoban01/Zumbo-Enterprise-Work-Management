using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService
{
    private static void Apply(
        KnowledgeDocument document,
        KnowledgeVersionDocument version)
    {
        document.Title = version.Title;
        document.ContentMarkdown = version.ContentMarkdown;
        document.Tags = [.. version.Tags];
        document.WorkItemIds = [.. version.WorkItemIds];
        document.UserIds = [.. version.UserIds];
        document.CurrentContentVersion = version.Number;
    }

    private static string Excerpt(string value)
    {
        var compact = WhitespacePattern().Replace(value, " ").Trim();
        return compact.Length <= 220 ? compact : compact[..217] + "...";
    }

    private static bool Matches(KnowledgeDocument document, string? query)
    {
        if (query is null) return true;
        return document.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || document.ContentMarkdown.Contains(query, StringComparison.OrdinalIgnoreCase)
            || document.Tags.Any(tag =>
                tag.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static KnowledgeDocumentResponse ToResponse(
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
                .Select(ToResponse)
                .ToList(),
            document.OwnerUserId == userId || access.CanManage,
            access.CanComment,
            document.Archived,
            document.UpdatedAt,
            document.Version);

    private static KnowledgeVersionResponse ToResponse(
        KnowledgeVersionDocument version) => new(
            version.Number,
            version.Title,
            version.ContentMarkdown,
            version.Tags,
            version.WorkItemIds,
            version.UserIds,
            version.ChangeSummary,
            version.AuthorUserId,
            version.CreatedAt);

    private static KnowledgeCommentResponse ToResponse(
        KnowledgeCommentDocument comment) => new(
            comment.Id,
            comment.Body,
            comment.AuthorUserId,
            comment.Resolved,
            comment.ResolvedByUserId,
            comment.ResolvedAt,
            comment.CreatedAt);

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

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
