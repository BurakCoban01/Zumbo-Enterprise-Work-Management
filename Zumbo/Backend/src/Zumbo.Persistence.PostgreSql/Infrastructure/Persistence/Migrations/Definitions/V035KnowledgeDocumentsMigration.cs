namespace Zumbo.Persistence.PostgreSql;

internal static class V035KnowledgeDocumentsMigration
{
        private const string UpSql = """
            CREATE TABLE IF NOT EXISTS projects.knowledge_documents (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_knowledge_tenant_scope_state
                ON projects.knowledge_documents (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ScopeType'),
                    (document ->> 'ScopeId'),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_knowledge_tenant_owner_state
                ON projects.knowledge_documents (
                    (document ->> 'OrganizationId'),
                    (document ->> 'OwnerUserId'),
                    ((document ->> 'Archived')::boolean),
                    id);
            CREATE INDEX IF NOT EXISTS ix_knowledge_tenant_tags
                ON projects.knowledge_documents
                USING gin ((document -> 'Tags'));
            """;

        private const string DownSql = """
            DROP TABLE IF EXISTS projects.knowledge_documents;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        35,
        "knowledge_documents",
        UpSql,
        DownSql);
}
