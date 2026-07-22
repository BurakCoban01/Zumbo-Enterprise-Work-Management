using Npgsql;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Notifications;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlNotificationDeliveryRepositoryContractTests(PostgreSqlFixture fixture)
    : NotificationDeliveryRepositoryContract
{
    [Fact]
    public async Task Migration22_CreatesTenantDedupeAndClaimIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'notifications'
              AND indexname IN (
                'ux_notifications_deduplication_key',
                'ix_notifications_email_status_next_attempt');
            """);
        Assert.Equal(2, indexes);
        Assert.Contains("22:notification_delivery_indexes",
            await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TenantDedupeUniqueIndexRejectsOnlySameTenantDuplicate()
    {
        var repository = Repository();
        var now = new DateTimeOffset(2026, 7, 20, 20, 0, 0, TimeSpan.Zero);
        await repository.CreateAsync(Pending("pg-dedupe-org-1-a", "org-db-unique-1", now));
        var duplicate = await Assert.ThrowsAsync<DocumentConflictException>(() =>
            repository.CreateAsync(Pending("pg-dedupe-org-1-b", "org-db-unique-1", now)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        await repository.CreateAsync(Pending("pg-dedupe-org-2", "org-db-unique-2", now));
    }

    protected override IDocumentRepository<NotificationDocument> Repository() =>
        fixture.Api.CreateRepository<NotificationDocument>("notifications", "notifications");

    private static NotificationDocument Pending(string id, string organizationId, DateTimeOffset now) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        UserId = "user-db-unique",
        Type = "Assignment",
        Message = "Assigned",
        EmailAddress = "user-db-unique@zumbo.local",
        EmailStatus = NotificationEmailStatuses.Pending,
        EmailNextAttemptAt = now,
        DeduplicationKey = "pg-shared-dedupe",
        CreatedAt = now
    };
}
