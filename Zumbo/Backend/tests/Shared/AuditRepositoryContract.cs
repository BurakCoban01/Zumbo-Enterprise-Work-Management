using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;

namespace Zumbo.RepositoryContracts;

public abstract class AuditRepositoryContract
{
    protected abstract IDocumentRepository<AuditLogDocument> Repository();

    [Fact]
    public async Task TenantCursorAndStructuredIntegrityFields_RoundTrip()
    {
        var repository = Repository();
        var stamp = new DateTimeOffset(2026, 7, 20, 18, 0, 0, TimeSpan.Zero);
        await repository.CreateAsync(Log("audit-a", "org-1", stamp, 1, null, "hash-a"));
        await repository.CreateAsync(Log("audit-b", "org-1", stamp, 2, "hash-a", "hash-b"));
        await repository.CreateAsync(Log("audit-c", "org-2", stamp, 1, null, "hash-c"));

        var first = await repository.ListByFilterAsync(
            x => x.OrganizationId == "org-1",
            x => x.CreatedAt,
            orderDescending: true,
            pageSize: 1);
        var cursor = Assert.Single(first);
        var second = await repository.ListByFilterAsync(
            x => x.OrganizationId == "org-1"
                && (x.CreatedAt < cursor.CreatedAt
                    || (x.CreatedAt == cursor.CreatedAt && x.Id.CompareTo(cursor.Id) > 0)),
            x => x.CreatedAt,
            orderDescending: true,
            pageSize: 2);

        var next = Assert.Single(second);
        Assert.Equal("audit-b", next.Id);
        Assert.Equal("hash-a", next.PreviousHash);
        Assert.Equal("hash-b", next.RecordHash);
        Assert.Equal("[REDACTED]", Assert.Single(next.Changes).NewValue);
        Assert.Equal(2, await repository.CountByFilterAsync(x => x.OrganizationId == "org-1"));
    }

    private static AuditLogDocument Log(
        string id,
        string organizationId,
        DateTimeOffset createdAt,
        long sequence,
        string? previousHash,
        string hash) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        ActorUserId = "actor",
        SubjectType = "WorkItem",
        SubjectId = "item",
        Action = "Updated",
        EntityType = "WorkItem",
        EntityId = "item",
        CorrelationId = "correlation",
        CreatedAt = createdAt,
        ChainSequence = sequence,
        PreviousHash = previousHash,
        RecordHash = hash,
        Changes = [new AuditChangeDocument { Field = "password", NewValue = "[REDACTED]", Redacted = true }]
    };
}
