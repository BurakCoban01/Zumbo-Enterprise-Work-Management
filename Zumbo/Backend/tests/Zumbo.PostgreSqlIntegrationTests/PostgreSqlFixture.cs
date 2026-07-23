using System.Data.Common;
using Npgsql;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Persistence.PostgreSql;
using Zumbo.RepositoryContracts;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.WorkItems;

namespace Zumbo.PostgreSqlIntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "DATA-004 PostgreSQL";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    public const string TestSchema = "data004_tests";
    public const string RepositoryTable = "repository_contract_documents";
    public const string TransactionTable = "transaction_probe";

    public PostgreSqlApi Api { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ZUMBO_TEST_POSTGRES_CONNECTION_STRING to the loopback PostgreSQL test instance.");
        }

        Api = PostgreSqlApi.Create(connectionString);
        await Api.ResetAndMigrateAsync(CancellationToken.None);
        await using (var bootstrap = new PostgreSqlProvider(connectionString))
        {
            _ = bootstrap.CreateRepository<RepositoryContractDocument>(TestSchema, RepositoryTable);
        }

        await using var connection = await Api.OpenConnectionAsync(CancellationToken.None);
        await ExecuteAsync(connection, $"""
            CREATE SCHEMA IF NOT EXISTS {TestSchema};
            CREATE TABLE IF NOT EXISTS {TestSchema}.{TransactionTable} (
                id uuid PRIMARY KEY,
                value text NOT NULL,
                created_at_utc timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS ix_transaction_probe_value
                ON {TestSchema}.{TransactionTable} (value);
            TRUNCATE TABLE {TestSchema}.{TransactionTable};
            """);
    }

    public async Task DisposeAsync()
    {
        if (Api is null)
        {
            return;
        }

        try
        {
            await using var connection = await Api.OpenConnectionAsync(CancellationToken.None);
            await ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS {TestSchema} CASCADE;");
        }
        finally
        {
            await Api.DisposeAsync();
        }
    }

    public static async Task ExecuteAsync(
        DbConnection connection,
        string sql,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<T> ScalarAsync<T>(
        DbConnection connection,
        string sql,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return (T)Convert.ChangeType(value!, typeof(T));
    }
}

public sealed class PostgreSqlApi : IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgreSqlPersistenceOptions options;
    private readonly PostgreSqlSession session;
    private readonly PostgreSqlMigrationRunner migrations;
    private readonly PostgreSqlTransactionRunner transactions;

    private PostgreSqlApi(
        NpgsqlDataSource dataSource,
        PostgreSqlPersistenceOptions options,
        PostgreSqlSession session)
    {
        this.dataSource = dataSource;
        this.options = options;
        this.session = session;
        migrations = new PostgreSqlMigrationRunner(dataSource, options);
        transactions = new PostgreSqlTransactionRunner(session);
        Outbox = new PostgreSqlDurableEventOutbox(session, options);
        Inbox = new PostgreSqlDurableEventInbox(session, options);
    }

    public IDurableEventOutbox Outbox { get; }
    public IDurableEventInbox Inbox { get; }
    public string ConnectionString => options.ConnectionString;

    public static PostgreSqlApi Create(string connectionString)
    {
        var options = new PostgreSqlPersistenceOptions
        {
            ConnectionString = connectionString,
            CommandTimeoutSeconds = 15
        };
        options.MapDocument<RepositoryContractDocument>(
            PostgreSqlFixture.TestSchema,
            PostgreSqlFixture.RepositoryTable);
        options.MapDocument<AuditLogDocument>("audit", "audit_logs");
        options.MapDocument<NotificationDocument>("notifications", "notifications");
        options.MapDocument<RefreshSessionDocument>("identity", "refresh_sessions");
        options.MapDocument<ApiKeyDocument>("identity", "api_keys");
        options.MapDocument<PrivacyWorkflowDocument>("identity", "privacy_workflows");
        options.MapDocument<WorkItemCommentActivityDocument>("work_items", "work_item_comments");
        options.MapDocument<WorkItemCommentRevisionActivityDocument>("work_items", "work_item_comment_revisions");
        options.MapDocument<WorkItemAttachmentActivityDocument>("work_items", "work_item_attachments");
        options.MapDocument<WorkItemWorkLogActivityDocument>("work_items", "work_item_work_logs");
        options.MapDocument<WorkItemApprovalActivityDocument>("work_items", "work_item_approvals");
        options.MapDocument<WorkItemTimelineActivityDocument>("work_items", "work_item_timeline");
        options.MapDocument<WorkItemRelationEdgeDocument>("work_items", "work_item_relation_edges");
        options.MapDocument<WorkItemCollaborationDocument>("work_items", "work_item_collaborations");
        options.MapDocument<WorkItemEventActivityDocument>("work_items", "work_item_event_activities");
        options.MapDocument<WorkItemTemplateDocument>("work_items", "work_item_templates");
        options.MapDocument<WorkItemRecurrenceDocument>("work_items", "work_item_recurrences");
        options.MapDocument<WorkItemRecurrenceOccurrenceDocument>("work_items", "work_item_recurrence_occurrences");
        options.MapDocument<WorkItemBulkJobDocument>("work_items", "work_item_bulk_jobs");
        options.MapDocument<WorkItemBulkJobItemDocument>("work_items", "work_item_bulk_job_items");
        options.MapDocument<IntakeFormDocument>("work_items", "intake_forms");
        options.MapDocument<IntakeFormVersionDocument>("work_items", "intake_form_versions");
        options.MapDocument<IntakeSubmissionDocument>("work_items", "intake_submissions");
        options.MapDocument<WebhookSubscriptionDocument>("work_items", "webhook_subscriptions");
        options.MapDocument<WebhookDeliveryDocument>("work_items", "webhook_deliveries");
        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        return new PostgreSqlApi(dataSource, options, new PostgreSqlSession(dataSource));
    }

    public IDocumentRepository<TDocument> CreateRepository<TDocument>(string schema, string table)
        where TDocument : class, IDocument
    {
        var configured = options.ResolveForTests(typeof(TDocument));
        Assert.Equal(new PostgreSqlDocumentStorage(schema, table), configured);
        return new PostgreSqlDocumentRepository<TDocument>(session, options);
    }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken) =>
        await dataSource.OpenConnectionAsync(cancellationToken);

    public Task MigrateAsync(CancellationToken cancellationToken) =>
        migrations.ApplyAsync(cancellationToken);

    public Task<string> GenerateMigrationScriptAsync(
        long? fromVersion,
        long? toVersion,
        bool idempotent,
        CancellationToken cancellationToken) =>
        migrations.GenerateScriptAsync(fromVersion, toVersion, idempotent, cancellationToken);

    public async Task ResetAndMigrateAsync(CancellationToken cancellationToken)
    {
        var status = await migrations.StatusAsync(cancellationToken);
        if (status.Applied.Count > 0)
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await PostgreSqlFixture.ExecuteAsync(
                connection,
                "TRUNCATE TABLE notifications.notifications;",
                cancellationToken: cancellationToken);
            await migrations.RollbackAsync(0, cancellationToken);
        }

        await migrations.ApplyAsync(cancellationToken);
    }

    public Task RollbackAsync(string migrationId, CancellationToken cancellationToken)
    {
        var separator = migrationId.IndexOf(':', StringComparison.Ordinal);
        var version = long.Parse(separator < 0 ? migrationId : migrationId[..separator]);
        return migrations.RollbackAsync(version - 1, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetAppliedMigrationsAsync(CancellationToken cancellationToken)
    {
        var status = await migrations.StatusAsync(cancellationToken);
        return status.Applied.Select(migration => $"{migration.Version}:{migration.Name}").ToArray();
    }

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(operation, cancellationToken: cancellationToken);

    public async Task ResetDurableMessagingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await PostgreSqlFixture.ExecuteAsync(
            connection,
            "TRUNCATE TABLE messaging.inbox_messages, messaging.outbox_messages;",
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await session.DisposeAsync();
        await dataSource.DisposeAsync();
    }
}

internal static class PostgreSqlOptionsTestAccess
{
    public static PostgreSqlDocumentStorage ResolveForTests(
        this PostgreSqlPersistenceOptions options,
        Type documentType) =>
        options.DocumentMappings.TryGetValue(documentType, out var storage)
            ? storage
            : throw new InvalidOperationException($"No test mapping exists for {documentType.FullName}.");
}
