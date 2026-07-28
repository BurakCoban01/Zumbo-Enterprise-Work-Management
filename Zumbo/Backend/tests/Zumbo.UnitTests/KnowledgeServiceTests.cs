using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class KnowledgeServiceTests
{
    [Fact]
    public async Task CreatesImmutableVersionsWithBoundedLinksAndSearchableCurrentContent()
    {
        var fixture = new Fixture();
        var document = await fixture.Service.CreateAsync(
            Request(),
            "correlation-1",
            CancellationToken.None);
        document = await fixture.Service.AddVersionAsync(
            document.Id,
            new CreateKnowledgeVersionRequest(
                "Authentication decision",
                "# Decision\nUse [the internal runbook](/runbooks/auth).",
                ["security", "decision"],
                ["work-item-1"],
                ["viewer-1"],
                "Documented the selected authentication boundary."),
            "correlation-2",
            CancellationToken.None);

        Assert.Equal(2, document.CurrentContentVersion);
        Assert.Equal([2, 1], document.Versions.Select(item => item.Number));
        var first = await fixture.Service.GetVersionAsync(
            document.Id,
            1,
            CancellationToken.None);
        Assert.Equal("# Context\nSynthetic project context.", first.ContentMarkdown);
        Assert.Equal("Initial project context.", first.ChangeSummary);

        var search = await fixture.Service.SearchAsync(
            "authentication",
            KnowledgeScopeTypes.Project,
            "project-1",
            false,
            1,
            20,
            CancellationToken.None);
        Assert.Equal(KnowledgeSourceStatuses.Ready, search.SourceStatus);
        Assert.Equal(1, search.VisibleTotal);
        Assert.Equal("Authentication decision", Assert.Single(search.Items).Title);
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
        var visible = await fixture.Service.GetAsync(
            document.Id,
            false,
            CancellationToken.None);
        Assert.False(visible.CanEdit);
        Assert.True(visible.CanComment);

        var commented = await fixture.Service.AddCommentAsync(
            document.Id,
            new AddKnowledgeCommentRequest("Please clarify the recovery path."),
            "correlation",
            CancellationToken.None);
        var comment = Assert.Single(commented.Comments);
        Assert.Equal("viewer-1", comment.AuthorUserId);

        var resolved = await fixture.Service.ResolveCommentAsync(
            document.Id,
            comment.Id,
            "correlation",
            CancellationToken.None);
        Assert.True(Assert.Single(resolved.Comments).Resolved);
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

        public Fixture()
        {
            Directory = new KnowledgeDirectory(Current);
            Service = new KnowledgeService(
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
