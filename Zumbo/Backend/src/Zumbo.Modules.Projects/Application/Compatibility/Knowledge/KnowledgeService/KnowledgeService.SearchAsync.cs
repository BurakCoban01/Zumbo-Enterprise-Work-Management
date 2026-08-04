using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    public async Task<KnowledgeSearchResponse> SearchAsync(
        string? query,
        string? scopeType,
        string? scopeId,
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var normalizedQuery = Optional(query, 100)?.ToLowerInvariant();
        var normalizedScopeType = string.IsNullOrWhiteSpace(scopeType)
            ? null
            : AllowedScope(scopeType);
        var normalizedScopeId = Optional(scopeId, 128);
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
                    && (includeArchived || !item.Archived),
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
                var access = await AuthorizeDocumentAsync(
                    candidate,
                    actor.OrganizationId,
                    ct);
                visible.Add((candidate, access));
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

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var ordered = visible
            .OrderByDescending(item => item.Document.UpdatedAt)
            .ThenBy(item => item.Document.Id, StringComparer.Ordinal)
            .ToList();
        return new KnowledgeSearchResponse(
            ordered
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(item => ToSummary(item.Document, item.Access, actor.UserId))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            ordered.Count,
            candidates.Count,
            partial ? KnowledgeSourceStatuses.Partial : KnowledgeSourceStatuses.Ready);
    }
}
