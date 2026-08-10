namespace Zumbo.Persistence.PostgreSql;

internal static class V028IntakeFormsAndSubmissionsMigration
{
        private const string UpSql = """
            CREATE TABLE IF NOT EXISTS work_items.intake_forms (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_intake_forms_public_id
                ON work_items.intake_forms ((document ->> 'PublicId'));
            CREATE INDEX IF NOT EXISTS ix_intake_forms_tenant_project_state
                ON work_items.intake_forms (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    (document ->> 'State'),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);

            CREATE TABLE IF NOT EXISTS work_items.intake_form_versions (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_intake_form_versions_number
                ON work_items.intake_form_versions (
                    (document ->> 'FormId'),
                    ((document ->> 'DefinitionVersion')::integer));

            CREATE TABLE IF NOT EXISTS work_items.intake_submissions (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_intake_submissions_idempotency
                ON work_items.intake_submissions (
                    (document ->> 'OrganizationId'),
                    (document ->> 'FormId'),
                    (document ->> 'SubmittedByUserId'),
                    (document ->> 'IdempotencyKeyHash'));
            CREATE INDEX IF NOT EXISTS ix_intake_submissions_triage
                ON work_items.intake_submissions (
                    (document ->> 'OrganizationId'),
                    (document ->> 'FormId'),
                    (document ->> 'State'),
                    public.zumbo_parse_timestamptz(document ->> 'CreatedAt') DESC,
                    id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_intake_submissions_work_item
                ON work_items.intake_submissions ((document ->> 'WorkItemId'));
            """;

        private const string DownSql = """
            DROP TABLE IF EXISTS work_items.intake_submissions;
            DROP TABLE IF EXISTS work_items.intake_form_versions;
            DROP TABLE IF EXISTS work_items.intake_forms;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        28,
        "intake_forms_and_submissions",
        UpSql,
        DownSql);
}
