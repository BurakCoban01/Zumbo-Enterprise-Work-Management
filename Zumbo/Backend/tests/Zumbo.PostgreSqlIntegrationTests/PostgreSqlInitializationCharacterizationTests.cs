using Npgsql;
using Zumbo.Persistence.PostgreSql;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

public sealed class PostgreSqlInitializationCharacterizationTests
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;" +
        "Timeout=1;Command Timeout=1;Pooling=false";

    [Fact]
    public async Task ConstructionAndRepositoryCreation_DoNotOpenAConnectionOrRunDdl()
    {
        await using var provider = new PostgreSqlProvider(UnreachableConnectionString);

        var repository = provider.CreateRepository<RepositoryContractDocument>(
            PostgreSqlFixture.TestSchema,
            PostgreSqlFixture.RepositoryTable);

        Assert.NotNull(repository);
    }

    [Fact]
    public async Task ExplicitMigration_ReportsReadinessFailure()
    {
        await using var provider = new PostgreSqlProvider(UnreachableConnectionString);

        await Assert.ThrowsAsync<NpgsqlException>(() =>
            provider.MigrateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExplicitMigration_HonorsPreCancelledStartup()
    {
        await using var provider = new PostgreSqlProvider(UnreachableConnectionString);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.MigrateAsync(cancellation.Token));
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlParallelMigrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ParallelExplicitMigrations_DeduplicateDdlAndLedgerWrites()
    {
        await using var migrator = new PostgreSqlSchemaMigrator(fixture.Api.ConnectionString);
        await migrator.RollbackAsync(0, CancellationToken.None);

        try
        {
            await using var first = new PostgreSqlProvider(fixture.Api.ConnectionString);
            await using var second = new PostgreSqlProvider(fixture.Api.ConnectionString);

            await Task.WhenAll(
                first.MigrateAsync(CancellationToken.None),
                second.MigrateAsync(CancellationToken.None));

            var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
            Assert.Equal(37, applied.Count);
            Assert.Equal(applied.Count, applied.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            await migrator.ApplyAsync(CancellationToken.None);
        }
    }
}
