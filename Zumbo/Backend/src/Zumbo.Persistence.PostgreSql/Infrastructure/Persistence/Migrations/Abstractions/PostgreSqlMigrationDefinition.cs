namespace Zumbo.Persistence.PostgreSql;

internal sealed record PostgreSqlMigrationDefinition(
    long Version,
    string Name,
    string UpSql,
    string DownSql);
