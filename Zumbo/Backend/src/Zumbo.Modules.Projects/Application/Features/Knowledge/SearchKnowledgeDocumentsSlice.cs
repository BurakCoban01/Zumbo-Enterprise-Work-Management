using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal sealed class SearchKnowledgeDocumentsSlice(
    IDocumentRepository<KnowledgeDocument> documents,
    IKnowledgeDirectory directory,
    ICurrentUser currentUser)
{
    private readonly KnowledgeReadAccess access = new(documents, directory, currentUser);

    internal async Task<KnowledgeSearchResponse> HandleAsync(
        SearchKnowledgeDocumentsQuery query,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var normalizedQuery = KnowledgeQueryInput.Optional(query.Query, 100)?.ToLowerInvariant();
        var normalizedScopeType = string.IsNullOrWhiteSpace(query.ScopeType)
            ? null
            : KnowledgeQueryInput.AllowedScope(query.ScopeType);
        var normalizedScopeId = KnowledgeQueryInput.Optional(query.ScopeId, 128);
        if (normalizedScopeType is null && normalizedScopeId is not null)
            throw new ValidationException("Knowledge scope type is required when scope id is provided.");

        var candidates = new List<KnowledgeDocument>();
        string? cursor = null;
        var partial = false;
        do
        {
            var remaining = KnowledgeLimits.MaximumSearchDocuments - candidates.Count;
            if (remaining <= 0)
            {
                partial = true;
                break;
            }
            var batch = await documents.ListByCursorAsync(
                item => item.OrganizationId == actor.OrganizationId
                    && (query.IncludeArchived || !item.Archived),
                cursor,
                Math.Min(100, remaining),
                ct);
            candidates.AddRange(batch.Items);
            cursor = batch.NextCursor;
            if (candidates.Count >= KnowledgeLimits.MaximumSearchDocuments && cursor is not null)
            {
                partial = true;
                break;
            }
        } while (cursor is not null);

        var visible = new List<(KnowledgeDocument Document, KnowledgeScopeAccess Access)>();
        foreach (var candidate in candidates)
        {
            if (normalizedScopeType is not null && candidate.ScopeType != normalizedScopeType)
                continue;
            if (normalizedScopeId is not null && candidate.ScopeId != normalizedScopeId)
                continue;
            if (!Matches(candidate, normalizedQuery))
                continue;
            try
            {
                var scopeAccess = await access.AuthorizeDocumentAsync(
                    candidate,
                    actor.OrganizationId,
                    ct);
                visible.Add((candidate, scopeAccess));
            }
            catch (NotFoundException)
            {
                // Search must not reveal documents whose current scope is no longer visible.
            }
            catch (ForbiddenException)
            {
                // Search must not reveal documents whose current scope is no longer visible.
            }
        }

        var normalizedPage = Math.Max(query.Page, 1);
        var normalizedPageSize = Math.Clamp(query.PageSize, 1, 100);
        var ordered = visible
            .OrderByDescending(item => item.Document.UpdatedAt)
            .ThenBy(item => item.Document.Id, StringComparer.Ordinal)
            .ToList();
        return new KnowledgeSearchResponse(
            ordered
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(item => KnowledgeResponseMapper.ToSummary(item.Document, item.Access, actor.UserId))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            ordered.Count,
            candidates.Count,
            partial ? KnowledgeSourceStatuses.Partial : KnowledgeSourceStatuses.Ready);
    }

    private static bool Matches(KnowledgeDocument document, string? query)
    {
        if (query is null) return true;
        return document.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || document.ContentMarkdown.Contains(query, StringComparison.OrdinalIgnoreCase)
            || document.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
