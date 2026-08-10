using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Projects.Application.Features.Knowledge;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class KnowledgeServiceTests
{
    [Fact]
    public async Task CreatesImmutableVersionsWithBoundedLinksAndSearchableCurrentContent()
    {
        var fixture = new Fixture();
        var document = await fixture.CreateDocumentHandler.HandleAsync(
            new CreateKnowledgeDocumentCommand(Request(), "correlation-1"),
            CancellationToken.None);
        document = await fixture.AddVersionHandler.HandleAsync(
            new AddKnowledgeVersionCommand(
                document.Id,
                new CreateKnowledgeVersionRequest(
                    "Authentication decision",
                    "# Decision\nUse [the internal runbook](/runbooks/auth).",
                    ["security", "decision"],
                    ["work-item-1"],
                    ["viewer-1"],
                    "Documented the selected authentication boundary."),
                "correlation-2"),
            CancellationToken.None);

        Assert.Equal(2, document.CurrentContentVersion);
        Assert.Equal([2, 1], document.Versions.Select(item => item.Number));
        var first = await fixture.GetVersionHandler.HandleAsync(
            new GetKnowledgeVersionQuery(document.Id, 1),
            CancellationToken.None);
        Assert.Equal("# Context\nSynthetic project context.", first.ContentMarkdown);
        Assert.Equal("Initial project context.", first.ChangeSummary);

        var search = await fixture.SearchHandler.HandleAsync(
            new SearchKnowledgeDocumentsQuery(
                "authentication",
                KnowledgeScopeTypes.Project,
                "project-1",
                false,
                1,
                20),
            CancellationToken.None);
        Assert.Equal(KnowledgeSourceStatuses.Ready, search.SourceStatus);
        Assert.Equal(1, search.VisibleTotal);
        Assert.Equal("Authentication decision", Assert.Single(search.Items).Title);
        var options = await fixture.GetLinkOptionsHandler.HandleAsync(
            new GetKnowledgeLinkOptionsQuery(
                KnowledgeScopeTypes.Project,
                "project-1",
                "Synthetic"),
            CancellationToken.None);
        Assert.Equal("work-item-1", Assert.Single(options.WorkItems).Id);
        Assert.Equal("viewer-1", Assert.Single(options.Users).Id);

        var compatibilityVersion = await fixture.Service.GetVersionAsync(
            document.Id,
            1,
            CancellationToken.None);
        Assert.Equal(first.Number, compatibilityVersion.Number);
        Assert.Equal(first.Title, compatibilityVersion.Title);
        Assert.Equal(first.ContentMarkdown, compatibilityVersion.ContentMarkdown);
        Assert.Equal(first.Tags, compatibilityVersion.Tags);
        Assert.Equal(first.WorkItemIds, compatibilityVersion.WorkItemIds);
        Assert.Equal(first.UserIds, compatibilityVersion.UserIds);
        Assert.Equal(first.ChangeSummary, compatibilityVersion.ChangeSummary);
        Assert.Equal(first.AuthorUserId, compatibilityVersion.AuthorUserId);
        Assert.Equal(first.CreatedAt, compatibilityVersion.CreatedAt);
        var compatibilitySearch = await fixture.Service.SearchAsync(
            "authentication",
            KnowledgeScopeTypes.Project,
            "project-1",
            false,
            1,
            20,
            CancellationToken.None);
        Assert.Equal(search.Page, compatibilitySearch.Page);
        Assert.Equal(search.PageSize, compatibilitySearch.PageSize);
        Assert.Equal(search.VisibleTotal, compatibilitySearch.VisibleTotal);
        Assert.Equal(search.ScannedDocuments, compatibilitySearch.ScannedDocuments);
        Assert.Equal(search.SourceStatus, compatibilitySearch.SourceStatus);
        Assert.Equal(
            search.Items.Select(item => item.Id),
            compatibilitySearch.Items.Select(item => item.Id));
        var compatibilityOptions = await fixture.Service.GetLinkOptionsAsync(
            KnowledgeScopeTypes.Project,
            "project-1",
            "Synthetic",
            CancellationToken.None);
        Assert.Equal(
            options.WorkItems.Select(item => item.Id),
            compatibilityOptions.WorkItems.Select(item => item.Id));
        Assert.Equal(
            options.Users.Select(item => item.Id),
            compatibilityOptions.Users.Select(item => item.Id));
        Assert.Equal(options.SourceStatus, compatibilityOptions.SourceStatus);

        await fixture.ArchiveDocumentHandler.HandleAsync(
            new ArchiveKnowledgeDocumentCommand(document.Id, "correlation-archive"),
            CancellationToken.None);
        var archived = await fixture.GetDocumentHandler.HandleAsync(
            new GetKnowledgeDocumentQuery(document.Id, IncludeArchived: true),
            CancellationToken.None);
        Assert.True(archived.Archived);
    }

    [Fact]
    public async Task ViewerCanReadAndCommentButCannotCreateVersion()
    {
        var fixture = new Fixture();
        var document = await fixture.Service.CreateAsync(
            Request(),
            "correlation",
            CancellationToken.None);

        fixture.Current.UserId = "viewer-1";
        fixture.Directory.Managers.Remove("viewer-1");
        var visible = await fixture.GetDocumentHandler.HandleAsync(
            new GetKnowledgeDocumentQuery(document.Id, IncludeArchived: false),
            CancellationToken.None);
        Assert.False(visible.CanEdit);
        Assert.True(visible.CanComment);
        var compatibilityVisible = await fixture.Service.GetAsync(
            document.Id,
            false,
            CancellationToken.None);
        Assert.Equal(visible.Id, compatibilityVisible.Id);
        Assert.Equal(visible.CanEdit, compatibilityVisible.CanEdit);
        Assert.Equal(visible.CanComment, compatibilityVisible.CanComment);
        Assert.Equal(visible.Version, compatibilityVisible.Version);
        Assert.Equal(
            visible.Versions.Select(item => item.Number),
            compatibilityVisible.Versions.Select(item => item.Number));
        Assert.Equal(
            visible.Comments.Select(item => item.Id),
            compatibilityVisible.Comments.Select(item => item.Id));

        var commented = await fixture.AddCommentHandler.HandleAsync(
            new AddKnowledgeCommentCommand(
                document.Id,
                new AddKnowledgeCommentRequest("Please clarify the recovery path."),
                "correlation"),
            CancellationToken.None);
        var comment = Assert.Single(commented.Comments);
        Assert.Equal("viewer-1", comment.AuthorUserId);

        var compatibilityCommented = await fixture.Service.AddCommentAsync(
            document.Id,
            new AddKnowledgeCommentRequest("Compatibility comment."),
            "correlation-compatibility",
            CancellationToken.None);
        Assert.Equal(2, compatibilityCommented.Comments.Count);

        var resolved = await fixture.ResolveCommentHandler.HandleAsync(
            new ResolveKnowledgeCommentCommand(
                document.Id,
                comment.Id,
                "correlation"),
            CancellationToken.None);
        Assert.True(resolved.Comments.Single(item => item.Id == comment.Id).Resolved);
        var compatibilityResolved = await fixture.Service.ResolveCommentAsync(
            document.Id,
            comment.Id,
            "correlation-compatibility",
            CancellationToken.None);
        Assert.True(compatibilityResolved.Comments.Single(item => item.Id == comment.Id).Resolved);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.AddVersionAsync(
                document.Id,
                VersionRequest(),
                "correlation",
                CancellationToken.None));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("[unsafe](javascript:alert(1))")]
    [InlineData("[unsafe](data:text/html,hello)")]
    public async Task RejectsRawHtmlAndUnsafeMarkdownTargets(string content)
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.CreateAsync(
                Request() with { ContentMarkdown = content },
                "correlation",
                CancellationToken.None));
    }

    [Fact]
    public async Task RejectsLinksOutsideScopeAndHidesLostScopeFromSearch()
    {
        var fixture = new Fixture();
        fixture.Directory.RejectLinks = true;
        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.CreateAsync(
                Request(),
                "correlation",
                CancellationToken.None));
        fixture.Directory.RejectLinks = false;
        var document = await fixture.Service.CreateAsync(
            Request(),
            "correlation",
            CancellationToken.None);
        fixture.Directory.DeniedScopes.Add(document.ScopeId);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.GetAsync(
                document.Id,
                false,
                CancellationToken.None));
        var search = await fixture.Service.SearchAsync(
            null,
            null,
            null,
            false,
            1,
            20,
            CancellationToken.None);
        Assert.Empty(search.Items);
        Assert.Equal(0, search.VisibleTotal);
        Assert.Equal(1, search.ScannedDocuments);
    }

    private static CreateKnowledgeDocumentRequest Request() =>
        new(
            KnowledgeScopeTypes.Project,
            "project-1",
            "Project context",
            "# Context\nSynthetic project context.",
            ["context"],
            ["work-item-1"],
            ["viewer-1"],
            "Initial project context.");

    private static CreateKnowledgeVersionRequest VersionRequest() =>
        new(
            "Updated project context",
            "# Updated\nSynthetic project context.",
            ["context"],
            ["work-item-1"],
            ["viewer-1"],
            "Updated project context.");

    private sealed class Fixture
    {
        public InMemoryDocumentRepository<KnowledgeDocument> Repository { get; } = new();
        public CurrentUser Current { get; } = new();
        public KnowledgeDirectory Directory { get; }
        public KnowledgeService Service { get; }
        public GetKnowledgeDocumentHandler GetDocumentHandler { get; }
        public GetKnowledgeVersionHandler GetVersionHandler { get; }
        public GetKnowledgeLinkOptionsHandler GetLinkOptionsHandler { get; }
        public SearchKnowledgeDocumentsHandler SearchHandler { get; }
        public AddKnowledgeCommentHandler AddCommentHandler { get; }
        public ResolveKnowledgeCommentHandler ResolveCommentHandler { get; }
        public CreateKnowledgeDocumentHandler CreateDocumentHandler { get; }
        public AddKnowledgeVersionHandler AddVersionHandler { get; }
        public ArchiveKnowledgeDocumentHandler ArchiveDocumentHandler { get; }

        public Fixture()
        {
            Directory = new KnowledgeDirectory(Current);
            Service = new KnowledgeService(
                Repository,
                Directory,
                new CapturingAudit(),
                Current,
                new FixedClock());
            GetDocumentHandler = new GetKnowledgeDocumentHandler(Repository, Directory, Current);
            GetVersionHandler = new GetKnowledgeVersionHandler(Repository, Directory, Current);
            GetLinkOptionsHandler = new GetKnowledgeLinkOptionsHandler(Directory, Current);
            SearchHandler = new SearchKnowledgeDocumentsHandler(Repository, Directory, Current);
            AddCommentHandler = new AddKnowledgeCommentHandler(
                Repository,
                Directory,
                new CapturingAudit(),
                Current,
                new FixedClock());
            ResolveCommentHandler = new ResolveKnowledgeCommentHandler(
                Repository,
                Directory,
                new CapturingAudit(),
                Current,
                new FixedClock());
            CreateDocumentHandler = new CreateKnowledgeDocumentHandler(
                Repository,
                Directory,
                new CapturingAudit(),
                Current,
                new FixedClock());
            AddVersionHandler = new AddKnowledgeVersionHandler(
                Repository,
                Directory,
                new CapturingAudit(),
                Current,
                new FixedClock());
            ArchiveDocumentHandler = new ArchiveKnowledgeDocumentHandler(
                Repository,
                Directory,
                new CapturingAudit(),
                Current,
                new FixedClock());
        }
    }

    private sealed class KnowledgeDirectory(CurrentUser current) : IKnowledgeDirectory
    {
        public HashSet<string> Managers { get; } =
            new(["owner-1"], StringComparer.Ordinal);
        public HashSet<string> DeniedScopes { get; } = new(StringComparer.Ordinal);
        public bool RejectLinks { get; set; }

        public Task<KnowledgeScopeAccess> AuthorizeScopeAsync(
            string scopeType,
            string scopeId,
            CancellationToken ct)
        {
            if (DeniedScopes.Contains(scopeId))
            {
                throw new NotFoundException(
                    "KNOWLEDGE_SCOPE_NOT_FOUND",
                    "Knowledge scope was not found.");
            }
            return Task.FromResult(new KnowledgeScopeAccess(
                "organization-1",
                "Synthetic project",
                ["project-1"],
                Managers.Contains(current.UserId ?? string.Empty),
                current.UserId is "owner-1" or "viewer-1"));
        }

        public Task EnsureLinksAsync(
            string organizationId,
            IReadOnlyCollection<string> scopeProjectIds,
            IReadOnlyCollection<string> workItemIds,
            IReadOnlyCollection<string> userIds,
            CancellationToken ct)
        {
            if (RejectLinks)
                throw new ValidationException("Knowledge link is outside the scope.");
            return Task.CompletedTask;
        }

        public Task<KnowledgeLinkOptionsResponse> ReadLinkOptionsAsync(
            string organizationId,
            IReadOnlyCollection<string> scopeProjectIds,
            string? query,
            CancellationToken ct) =>
            Task.FromResult(new KnowledgeLinkOptionsResponse(
                [new KnowledgeLinkOptionResponse(
                    "work-item-1",
                    "Synthetic work item",
                    "project-1")],
                [new KnowledgeLinkOptionResponse(
                    "viewer-1",
                    "Viewer",
                    "viewer@zumbo.local")],
                KnowledgeSourceStatuses.Ready));
    }

    private sealed class CapturingAudit : IKnowledgeAuditWriter
    {
        public Task WriteAsync(
            string action,
            string documentId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CurrentUser : ICurrentUser
    {
        public string? UserId { get; set; } = "owner-1";
        public string? OrganizationId => "organization-1";
        public IReadOnlyCollection<string> Roles => ["User"];
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
    }
}
