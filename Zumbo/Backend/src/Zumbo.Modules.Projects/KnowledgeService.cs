using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record CreateKnowledgeDocumentRequest(
    string ScopeType,
    string ScopeId,
    string Title,
    string ContentMarkdown,
    IReadOnlyCollection<string> Tags,
    IReadOnlyCollection<string> WorkItemIds,
    IReadOnlyCollection<string> UserIds,
    string ChangeSummary);

public sealed record CreateKnowledgeVersionRequest(
    string Title,
    string ContentMarkdown,
    IReadOnlyCollection<string> Tags,
    IReadOnlyCollection<string> WorkItemIds,
    IReadOnlyCollection<string> UserIds,
    string ChangeSummary);

public sealed record AddKnowledgeCommentRequest(string Body);

public sealed record KnowledgeScopeAccess(
    string OrganizationId,
    string ScopeName,
    IReadOnlyCollection<string> ProjectIds,
    bool CanManage,
    bool CanComment);

public sealed record KnowledgeVersionSummaryResponse(
    int Number,
    string Title,
    string ChangeSummary,
    string AuthorUserId,
    DateTimeOffset CreatedAt);

public sealed record KnowledgeVersionResponse(
    int Number,
    string Title,
    string ContentMarkdown,
    IReadOnlyCollection<string> Tags,
    IReadOnlyCollection<string> WorkItemIds,
    IReadOnlyCollection<string> UserIds,
    string ChangeSummary,
    string AuthorUserId,
    DateTimeOffset CreatedAt);

public sealed record KnowledgeCommentResponse(
    string Id,
    string Body,
    string AuthorUserId,
    bool Resolved,
    string? ResolvedByUserId,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt);

public sealed record KnowledgeDocumentResponse(
    string Id,
    string ScopeType,
    string ScopeId,
    string ScopeName,
    string OwnerUserId,
    string Title,
    string ContentMarkdown,
    IReadOnlyCollection<string> Tags,
    IReadOnlyCollection<string> WorkItemIds,
    IReadOnlyCollection<string> UserIds,
    int CurrentContentVersion,
    IReadOnlyCollection<KnowledgeVersionSummaryResponse> Versions,
    IReadOnlyCollection<KnowledgeCommentResponse> Comments,
    bool CanEdit,
    bool CanComment,
    bool Archived,
    DateTimeOffset UpdatedAt,
    long Version) : IVersionedResource;

public sealed record KnowledgeDocumentSummaryResponse(
    string Id,
    string ScopeType,
    string ScopeId,
    string ScopeName,
    string OwnerUserId,
    string Title,
    string Excerpt,
    IReadOnlyCollection<string> Tags,
    int CurrentContentVersion,
    bool CanEdit,
    bool Archived,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record KnowledgeSearchResponse(
    IReadOnlyCollection<KnowledgeDocumentSummaryResponse> Items,
    int Page,
    int PageSize,
    long VisibleTotal,
    int ScannedDocuments,
    string SourceStatus);

public sealed record KnowledgeLinkOptionResponse(
    string Id,
    string Label,
    string? Context);

public sealed record KnowledgeLinkOptionsResponse(
    IReadOnlyCollection<KnowledgeLinkOptionResponse> WorkItems,
    IReadOnlyCollection<KnowledgeLinkOptionResponse> Users,
    string SourceStatus);

public interface IKnowledgeDirectory
{
    Task<KnowledgeScopeAccess> AuthorizeScopeAsync(
        string scopeType,
        string scopeId,
        CancellationToken ct);

    Task EnsureLinksAsync(
        string organizationId,
        IReadOnlyCollection<string> scopeProjectIds,
        IReadOnlyCollection<string> workItemIds,
        IReadOnlyCollection<string> userIds,
        CancellationToken ct);

    Task<KnowledgeLinkOptionsResponse> ReadLinkOptionsAsync(
        string organizationId,
        IReadOnlyCollection<string> scopeProjectIds,
        string? query,
        CancellationToken ct);
}

public interface IKnowledgeAuditWriter
{
    Task WriteAsync(
        string action,
        string documentId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}

public sealed partial class KnowledgeService(
    IDocumentRepository<KnowledgeDocument> documents,
    IKnowledgeDirectory directory,
    IKnowledgeAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<KnowledgeDocumentResponse> CreateAsync(
        CreateKnowledgeDocumentRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var normalizedScopeType = AllowedScope(request.ScopeType);
        var scopeId = Required(request.ScopeId, "Knowledge scope", 128);
        var access = await directory.AuthorizeScopeAsync(normalizedScopeType, scopeId, ct);
        EnsureOrganization(access.OrganizationId, actor.OrganizationId);
        if (!access.CanManage)
            throw new ForbiddenException("Project or initiative management access is required.");

        var version = NormalizeVersion(
            request.Title,
            request.ContentMarkdown,
            request.Tags,
            request.WorkItemIds,
            request.UserIds,
            request.ChangeSummary,
            actor.UserId,
            1,
            clock.UtcNow);
        await directory.EnsureLinksAsync(
            actor.OrganizationId,
            access.ProjectIds,
            version.WorkItemIds,
            version.UserIds,
            ct);

        var document = new KnowledgeDocument
        {
            OrganizationId = actor.OrganizationId,
            ScopeType = normalizedScopeType,
            ScopeId = scopeId,
            ScopeName = access.ScopeName,
            OwnerUserId = actor.UserId,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        Apply(document, version);
        document.Versions.Add(version);
        document = await documents.CreateAsync(document, ct);
        await audit.WriteAsync(
            "KnowledgeDocumentCreated",
            document.Id,
            null,
            document.Title,
            correlationId,
            ct);
        return ToResponse(document, access, actor.UserId);
    }

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

    public async Task<KnowledgeDocumentResponse> GetAsync(
        string documentId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var document = await GetDocumentAsync(documentId, includeArchived, ct);
        var access = await AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        return ToResponse(document, access, actor.UserId);
    }

    public async Task<KnowledgeVersionResponse> GetVersionAsync(
        string documentId,
        int number,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var document = await GetDocumentAsync(documentId, includeArchived: true, ct);
        _ = await AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        var version = document.Versions.SingleOrDefault(item => item.Number == number)
            ?? throw new NotFoundException(
                "KNOWLEDGE_VERSION_NOT_FOUND",
                "Knowledge document version was not found.");
        return ToResponse(version);
    }

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

    public async Task<KnowledgeLinkOptionsResponse> GetLinkOptionsAsync(
        string scopeType,
        string scopeId,
        string? query,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var access = await directory.AuthorizeScopeAsync(
            AllowedScope(scopeType),
            Required(scopeId, "Knowledge scope", 128),
            ct);
        EnsureOrganization(access.OrganizationId, actor.OrganizationId);
        return await directory.ReadLinkOptionsAsync(
            actor.OrganizationId,
            access.ProjectIds,
            Optional(query, 100),
            ct);
    }

    public async Task<KnowledgeDocumentResponse> AddCommentAsync(
        string documentId,
        AddKnowledgeCommentRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var document = await GetDocumentAsync(documentId, includeArchived: false, ct);
        var access = await AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        if (!access.CanComment)
            throw new ForbiddenException("Knowledge comment access is required.");
        if (document.Comments.Count >= KnowledgeLimits.MaximumComments)
        {
            throw new ValidationException(
                $"A knowledge document cannot contain more than {KnowledgeLimits.MaximumComments} comments.");
        }

        var comment = new KnowledgeCommentDocument
        {
            Body = Required(request.Body, "Knowledge comment", 2_000),
            AuthorUserId = actor.UserId,
            CreatedAt = clock.UtcNow
        };
        document.Comments.Add(comment);
        document.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(document, ct);
        await audit.WriteAsync(
            "KnowledgeCommentCreated",
            document.Id,
            null,
            comment.Id,
            correlationId,
            ct);
        return ToResponse(document, access, actor.UserId);
    }

    public async Task<KnowledgeDocumentResponse> ResolveCommentAsync(
        string documentId,
        string commentId,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var document = await GetDocumentAsync(documentId, includeArchived: false, ct);
        var access = await AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        var comment = document.Comments.SingleOrDefault(item => item.Id == commentId)
            ?? throw new NotFoundException(
                "KNOWLEDGE_COMMENT_NOT_FOUND",
                "Knowledge comment was not found.");
        if (comment.AuthorUserId != actor.UserId
            && document.OwnerUserId != actor.UserId
            && !access.CanManage)
        {
            throw new ForbiddenException(
                "Only the comment author or a document manager can resolve this comment.");
        }
        if (!comment.Resolved)
        {
            comment.Resolved = true;
            comment.ResolvedByUserId = actor.UserId;
            comment.ResolvedAt = clock.UtcNow;
            document.UpdatedAt = clock.UtcNow;
            await ReplaceAsync(document, ct);
            await audit.WriteAsync(
                "KnowledgeCommentResolved",
                document.Id,
                comment.Id,
                "Resolved",
                correlationId,
                ct);
        }
        return ToResponse(document, access, actor.UserId);
    }

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

    private async Task<KnowledgeDocument> GetDocumentAsync(
        string documentId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        return await documents.SelectAsync(
            item => item.Id == documentId
                && item.OrganizationId == actor.OrganizationId
                && (includeArchived || !item.Archived),
            ct)
            ?? throw new NotFoundException(
                "KNOWLEDGE_DOCUMENT_NOT_FOUND",
                "Knowledge document was not found.");
    }

    private async Task<KnowledgeScopeAccess> AuthorizeDocumentAsync(
        KnowledgeDocument document,
        string organizationId,
        CancellationToken ct)
    {
        EnsureOrganization(document.OrganizationId, organizationId);
        var access = await directory.AuthorizeScopeAsync(
            document.ScopeType,
            document.ScopeId,
            ct);
        EnsureOrganization(access.OrganizationId, organizationId);
        return access;
    }

    private async Task ReplaceAsync(KnowledgeDocument document, CancellationToken ct)
    {
        var result = await documents.ReplaceByVersionAsync(
            item => item.Id == document.Id
                && item.OrganizationId == document.OrganizationId,
            document,
            expectedVersion.Consume(document.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException(
                "KNOWLEDGE_DOCUMENT_NOT_FOUND",
                "Knowledge document was not found.");
        }
        document.Version = result.Version!.Value;
    }

    private (string UserId, string OrganizationId) CurrentActor() => (
        currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required."),
        currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required."));

    private static KnowledgeVersionDocument NormalizeVersion(
        string? title,
        string? contentMarkdown,
        IReadOnlyCollection<string>? tags,
        IReadOnlyCollection<string>? workItemIds,
        IReadOnlyCollection<string>? userIds,
        string? changeSummary,
        string authorUserId,
        int number,
        DateTimeOffset createdAt)
    {
        var content = NormalizeContent(contentMarkdown);
        return new KnowledgeVersionDocument
        {
            Number = number,
            Title = Required(title, "Knowledge document title", 160),
            ContentMarkdown = content,
            Tags = NormalizeLabels(tags, KnowledgeLimits.MaximumTags),
            WorkItemIds = NormalizeIds(
                workItemIds,
                KnowledgeLimits.MaximumWorkItemLinks,
                "Knowledge work-item link"),
            UserIds = NormalizeIds(
                userIds,
                KnowledgeLimits.MaximumUserLinks,
                "Knowledge user link"),
            ChangeSummary = Required(changeSummary, "Knowledge version summary", 500),
            AuthorUserId = authorUserId,
            CreatedAt = createdAt
        };
    }

    private static string NormalizeContent(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (normalized.Length > KnowledgeLimits.MaximumContentCharacters)
        {
            throw new ValidationException(
                $"Knowledge content cannot exceed {KnowledgeLimits.MaximumContentCharacters} characters.");
        }
        if (normalized.Any(character =>
                char.IsControl(character) && character is not ('\n' or '\t')))
        {
            throw new ValidationException("Knowledge content contains unsupported control characters.");
        }
        if (RawHtmlPattern().IsMatch(normalized))
            throw new ValidationException("Raw HTML is not supported in knowledge content.");
        foreach (Match match in MarkdownLinkPattern().Matches(normalized))
            ValidateLinkTarget(match.Groups["target"].Value);
        return normalized;
    }

    private static void ValidateLinkTarget(string target)
    {
        var normalized = target.Trim().Trim('<', '>');
        if (normalized.StartsWith('/') || normalized.StartsWith('#'))
            return;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ValidationException(
                "Knowledge links must use HTTPS, HTTP, an internal path or an anchor.");
        }
    }

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

    private static bool Matches(KnowledgeDocument document, string? query)
    {
        if (query is null) return true;
        return document.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || document.ContentMarkdown.Contains(query, StringComparison.OrdinalIgnoreCase)
            || document.Tags.Any(tag =>
                tag.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureCanEdit(
        KnowledgeDocument document,
        KnowledgeScopeAccess access,
        string userId)
    {
        if (document.OwnerUserId != userId && !access.CanManage)
            throw new ForbiddenException("Knowledge document edit access is required.");
    }

    private static void EnsureOrganization(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new NotFoundException(
                "KNOWLEDGE_DOCUMENT_NOT_FOUND",
                "Knowledge document was not found.");
        }
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

    private static string Excerpt(string value)
    {
        var compact = WhitespacePattern().Replace(value, " ").Trim();
        return compact.Length <= 220 ? compact : compact[..217] + "...";
    }

    private static string AllowedScope(string? value)
    {
        var normalized = Required(value, "Knowledge scope type", 32);
        return KnowledgeScopeTypes.Allowed.Contains(normalized)
            ? normalized
            : throw new ValidationException("Knowledge scope type is not supported.");
    }

    private static List<string> NormalizeLabels(
        IReadOnlyCollection<string>? values,
        int maximum)
    {
        var normalized = (values
                ?? throw new ValidationException("Knowledge tag list is required."))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count > maximum
            || normalized.Any(value => value.Length > 32))
        {
            throw new ValidationException("Knowledge tag list is outside the supported bounds.");
        }
        return normalized;
    }

    private static List<string> NormalizeIds(
        IReadOnlyCollection<string>? values,
        int maximum,
        string label)
    {
        var normalized = (values
                ?? throw new ValidationException($"{label} list is required."))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalized.Count > maximum
            || normalized.Any(value => value.Length > 128))
        {
            throw new ValidationException($"{label} list is outside the supported bounds.");
        }
        return normalized;
    }

    private static string Required(string? value, string label, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{label} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"{label} cannot exceed {maximum} characters.");
        return normalized;
    }

    private static string? Optional(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"Value cannot exceed {maximum} characters.");
        return normalized;
    }

    [GeneratedRegex(@"<\s*/?\s*[A-Za-z]", RegexOptions.CultureInvariant)]
    private static partial Regex RawHtmlPattern();

    [GeneratedRegex(
        @"!?\[[^\]\r\n]{0,500}\]\(\s*(?<target>[^\s\)]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
