namespace Zumbo.Persistence.PostgreSql;

internal static class V001CreateModuleSchemasMigration
{
    internal static PostgreSqlMigrationDefinition Create(string upSql, string downSql) => new(
        1,
        "create_module_schemas",
        upSql,
        downSql);
}
