using System.Data.Common;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlProviderAcceptanceTests(PostgreSqlFixture fixture)
{
    private static readonly string[] RequiredSchemas =
    [
        "audit",
        "boards",
        "identity",
        "notifications",
        "organizations",
        "projects",
        "teams",
        "work_items",
        "workflows"
    ];

    [Fact]
    public async Task ProfileHealth_IsReadyWritableAndNotARecoveryReplica()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);

        Assert.Equal("Open", connection.State.ToString());
        Assert.False(await PostgreSqlFixture.ScalarAsync<bool>(connection, "SELECT pg_is_in_recovery();"));
        Assert.Equal(1, await PostgreSqlFixture.ScalarAsync<int>(connection, "SELECT 1;"));

        var marker = Guid.NewGuid();
        await PostgreSqlFixture.ExecuteAsync(connection, $"""
            INSERT INTO {PostgreSqlFixture.TestSchema}.{PostgreSqlFixture.TransactionTable} (id, value)
            VALUES ('{marker}', 'health');
            DELETE FROM {PostgreSqlFixture.TestSchema}.{PostgreSqlFixture.TransactionTable}
            WHERE id = '{marker}';
            """);
    }

    [Fact]
    public async Task Migrations_CreateEveryModuleSchemaWithConstraintsAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var schemas = await ReadStringsAsync(connection, """
            SELECT schema_name
            FROM information_schema.schemata
            WHERE schema_name IN ('audit', 'boards', 'identity', 'notifications', 'organizations',
                                  'projects', 'teams', 'work_items', 'workflows')
            ORDER BY schema_name;
            """);

        Assert.Equal(RequiredSchemas, schemas);

        var tableInventory = await ReadInventoryAsync(connection, """
            SELECT schemaname, count(*)::bigint
            FROM pg_catalog.pg_tables
            WHERE schemaname IN ('audit', 'boards', 'identity', 'notifications', 'organizations',
                                 'projects', 'teams', 'work_items', 'workflows')
            GROUP BY schemaname
            ORDER BY schemaname;
            """);
        AssertInventoryCoversEverySchema(tableInventory, "table");

        var primaryKeys = await ReadInventoryAsync(connection, """
            SELECT n.nspname, count(*)::bigint
            FROM pg_catalog.pg_constraint c
            JOIN pg_catalog.pg_class t ON t.oid = c.conrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
            WHERE c.contype = 'p'
              AND n.nspname IN ('audit', 'boards', 'identity', 'notifications', 'organizations',
                                'projects', 'teams', 'work_items', 'workflows')
            GROUP BY n.nspname
            ORDER BY n.nspname;
            """);
        Assert.Equal(tableInventory, primaryKeys);

        var checks = await ReadInventoryAsync(connection, """
            SELECT n.nspname, count(*)::bigint
            FROM pg_catalog.pg_constraint c
            JOIN pg_catalog.pg_class t ON t.oid = c.conrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
            WHERE c.contype IN ('c', 'u', 'f')
              AND n.nspname IN ('audit', 'boards', 'identity', 'notifications', 'organizations',
                                'projects', 'teams', 'work_items', 'workflows')
            GROUP BY n.nspname
            ORDER BY n.nspname;
            """);
        AssertInventoryCoversEverySchema(checks, "check/unique/foreign-key constraint");

        var indexes = await ReadInventoryAsync(connection, """
            SELECT schemaname, count(*)::bigint
            FROM pg_catalog.pg_indexes
            WHERE schemaname IN ('audit', 'boards', 'identity', 'notifications', 'organizations',
                                 'projects', 'teams', 'work_items', 'workflows')
            GROUP BY schemaname
            ORDER BY schemaname;
            """);
        AssertInventoryCoversEverySchema(indexes, "index");

        var migrations = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.NotEmpty(migrations);
        Assert.DoesNotContain(migrations, string.IsNullOrWhiteSpace);
        Assert.Equal(migrations.Count, migrations.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TransactionCommit_PersistsAllWritesAtomically()
    {
        var repository = Repository();
        var first = $"transaction-commit-{Guid.NewGuid():N}";
        var second = $"transaction-commit-{Guid.NewGuid():N}";

        await fixture.Api.ExecuteInTransactionAsync(async token =>
        {
            await repository.CreateAsync(new RepositoryContractDocument { Id = first, Name = "commit-a" }, token);
            await repository.CreateAsync(new RepositoryContractDocument { Id = second, Name = "commit-b" }, token);
        }, CancellationToken.None);

        Assert.Equal(2, await repository.CountByFilterAsync(document =>
            document.Id == first || document.Id == second));
        await repository.DeleteByFilterAsync(document => document.Id == first || document.Id == second);
    }

    [Fact]
    public async Task TransactionFailure_RollsBackEveryWrite()
    {
        var repository = Repository();
        var id = $"transaction-rollback-{Guid.NewGuid():N}";

        await Assert.ThrowsAsync<IntentionalTransactionFailure>(() =>
            fixture.Api.ExecuteInTransactionAsync(async token =>
            {
                await repository.CreateAsync(
                    new RepositoryContractDocument { Id = id, Name = "must-rollback" }, token);
                throw new IntentionalTransactionFailure();
            }, CancellationToken.None));

        Assert.False(await repository.ExistsByFilterAsync(document => document.Id == id));
    }

    [Fact]
    public async Task TransactionCancellation_CancelsCommandAndRollsBack()
    {
        var repository = Repository();
        var id = $"transaction-cancel-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Api.ExecuteInTransactionAsync(async token =>
            {
                await repository.CreateAsync(
                    new RepositoryContractDocument { Id = id, Name = "cancelled" }, token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }, timeout.Token));

        Assert.False(await repository.ExistsByFilterAsync(document => document.Id == id));
    }

    [Fact]
    public async Task LatestMigration_CanRollbackAndReapplyWithoutLedgerDrift()
    {
        var before = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        var latest = Assert.Single(before.TakeLast(1));

        await fixture.Api.RollbackAsync(latest, CancellationToken.None);
        var rolledBack = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.DoesNotContain(latest, rolledBack);
        Assert.Equal(before.Count - 1, rolledBack.Count);

        await fixture.Api.MigrateAsync(CancellationToken.None);
        var reapplied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Equal(before, reapplied);
    }

    [Fact]
    public async Task MigrationScript_IsIdempotentAndDoesNotMutateTheLedger()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var before = await PostgreSqlFixture.ScalarAsync<long>(
            connection,
            "SELECT count(*) FROM public.zumbo_schema_migrations;");

        var script = await fixture.Api.GenerateMigrationScriptAsync(
            fromVersion: 3,
            toVersion: 4,
            idempotent: true,
            cancellationToken: CancellationToken.None);

        var after = await PostgreSqlFixture.ScalarAsync<long>(
            connection,
            "SELECT count(*) FROM public.zumbo_schema_migrations;");
        Assert.Equal(before, after);
        Assert.Contains("Migration 4: create_access_pattern_indexes", script, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (version) DO NOTHING", script, StringComparison.Ordinal);
        Assert.Contains("BEGIN;", script, StringComparison.Ordinal);
        Assert.Contains("COMMIT;", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepresentativeFilteredQuery_UsesDeclaredBtreeIndex()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        await PostgreSqlFixture.ExecuteAsync(connection, $"""
            INSERT INTO {PostgreSqlFixture.TestSchema}.{PostgreSqlFixture.TransactionTable} (id, value)
            SELECT gen_random_uuid(), 'plan-' || value
            FROM generate_series(1, 2000) AS value;
            ANALYZE {PostgreSqlFixture.TestSchema}.{PostgreSqlFixture.TransactionTable};
            """);

        var plan = await ReadStringsAsync(connection, $"""
            EXPLAIN (COSTS OFF)
            SELECT id
            FROM {PostgreSqlFixture.TestSchema}.{PostgreSqlFixture.TransactionTable}
            WHERE value = 'plan-1999';
            """);
        var rendered = string.Join(Environment.NewLine, plan);

        Assert.Contains("Index", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix_transaction_probe_value", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CriticalTeamListQuery_UsesItsAccessPatternIndex()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        await PostgreSqlFixture.ExecuteAsync(connection, """
            INSERT INTO teams.teams (id, version, document)
            SELECT
                'plan-team-' || value,
                1,
                jsonb_build_object(
                    'Id', 'plan-team-' || value,
                    'Version', 1,
                    'OrganizationId', CASE WHEN value <= 20 THEN 'plan-target' ELSE 'plan-other' END,
                    'Name', 'plan-name-' || lpad(value::text, 5, '0'),
                    'Archived', false)
            FROM generate_series(1, 2000) AS value;
            ANALYZE teams.teams;
            """);

        try
        {
            var plan = await ReadStringsAsync(connection, """
                EXPLAIN (COSTS OFF)
                SELECT document
                FROM teams.teams
                WHERE (document #>> ARRAY['OrganizationId']) = 'plan-target'
                  AND ((document #>> ARRAY['Archived'])::boolean) = false
                ORDER BY (document #>> ARRAY['Name']), id
                LIMIT 20;
                """);
            var rendered = string.Join(Environment.NewLine, plan);

            Assert.Contains("Index", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ix_teams_organization_archived_name", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("Seq Scan", rendered, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PostgreSqlFixture.ExecuteAsync(
                connection,
                "DELETE FROM teams.teams WHERE id LIKE 'plan-team-%';");
        }
    }

    private Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<RepositoryContractDocument>
        Repository() => fixture.Api.CreateRepository<RepositoryContractDocument>(
            PostgreSqlFixture.TestSchema,
            PostgreSqlFixture.RepositoryTable);

    private static void AssertInventoryCoversEverySchema(
        IReadOnlyDictionary<string, long> inventory,
        string item)
    {
        Assert.Equal(RequiredSchemas, inventory.Keys.Order(StringComparer.Ordinal));
        Assert.All(inventory, entry =>
            Assert.True(entry.Value > 0, $"Schema '{entry.Key}' has no {item}."));
    }

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<IReadOnlyDictionary<string, long>> ReadInventoryAsync(
        DbConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new SortedDictionary<string, long>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0), reader.GetInt64(1));
        }

        return values;
    }

    private sealed class IntentionalTransactionFailure : Exception
    {
    }
}
