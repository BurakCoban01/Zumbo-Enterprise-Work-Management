using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;

namespace Zumbo.RepositoryContracts;

public abstract class KnowledgeRepositoryContract
{
    protected abstract IDocumentRepository<KnowledgeDocument> Documents();

    [Fact]
    public async Task StorePreservesVersionsLinksCommentsAndCompareExchange()
    {
        var repository = Documents();
        var prefix = "feature007-contract-" + Guid.NewGuid().ToString("N");
        var document = new KnowledgeDocument
        {
            Id = prefix + "-document",
            OrganizationId = prefix + "-organization",
            ScopeType = KnowledgeScopeTypes.Project,
            ScopeId = prefix + "-project",
            ScopeName = "Provider project",
            OwnerUserId = prefix + "-owner",
            Title = "Provider specification",
            ContentMarkdown = "# Provider specification",
            Tags = ["architecture"],
            WorkItemIds = [prefix + "-work-item"],
            UserIds = [prefix + "-viewer"],
            CurrentContentVersion = 1,
            Versions =
            [
                new KnowledgeVersionDocument
                {
                    Number = 1,
                    Title = "Provider specification",
                    ContentMarkdown = "# Provider specification",
                    Tags = ["architecture"],
                    WorkItemIds = [prefix + "-work-item"],
                    UserIds = [prefix + "-viewer"],
                    ChangeSummary = "Initial specification",
                    AuthorUserId = prefix + "-owner",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ],
            Comments =
            [
                new KnowledgeCommentDocument
                {
                    Id = prefix + "-comment",
                    Body = "Provider comment",
                    AuthorUserId = prefix + "-viewer",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            document = await repository.CreateAsync(document);
            var stale = await repository.SelectAsync(item => item.Id == document.Id);
            var loaded = await repository.SelectAsync(item =>
                item.Id == document.Id
                && item.OrganizationId == document.OrganizationId
                && item.ScopeId == document.ScopeId);
            Assert.NotNull(loaded);
            Assert.Equal("# Provider specification", Assert.Single(loaded.Versions).ContentMarkdown);
            Assert.Equal(prefix + "-work-item", Assert.Single(loaded.WorkItemIds));
            Assert.Equal(prefix + "-viewer", Assert.Single(loaded.UserIds));
            Assert.Equal("Provider comment", Assert.Single(loaded.Comments).Body);

            document.Title = "Updated provider specification";
            document.UpdatedAt = document.UpdatedAt.AddMinutes(1);
            var replaced = await repository.ReplaceByVersionAsync(
                item => item.Id == document.Id
                    && item.OrganizationId == document.OrganizationId,
                document,
                document.Version);
            Assert.True(replaced.Found);
            document.Version = replaced.Version!.Value;

            stale!.Archived = true;
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                repository.ReplaceByVersionAsync(
                    item => item.Id == stale.Id,
                    stale,
                    stale.Version));
            Assert.Null(await repository.SelectAsync(item =>
                item.Id == document.Id
                && item.OrganizationId == prefix + "-foreign"));
        }
        finally
        {
            await repository.DeleteByFilterAsync(item => item.Id == document.Id);
        }
    }
}
