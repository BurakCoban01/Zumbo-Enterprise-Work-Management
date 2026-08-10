namespace Zumbo.Persistence.PostgreSql;

internal static class V002CreateDocumentTablesMigration
{
    internal static PostgreSqlMigrationDefinition Create(string upSql, string downSql) => new(
        2,
        "create_document_tables",
        upSql,
        downSql);
}
