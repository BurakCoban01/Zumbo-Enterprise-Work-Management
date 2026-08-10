namespace Zumbo.Persistence.PostgreSql;

internal static class V003CreateJsonbIndexesMigration
{
    internal static PostgreSqlMigrationDefinition Create(string upSql, string downSql) => new(
        3,
        "create_jsonb_indexes",
        upSql,
        downSql);
}
