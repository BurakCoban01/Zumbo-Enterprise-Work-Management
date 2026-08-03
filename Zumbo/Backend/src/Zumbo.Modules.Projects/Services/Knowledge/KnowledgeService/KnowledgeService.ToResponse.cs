using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

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
}
