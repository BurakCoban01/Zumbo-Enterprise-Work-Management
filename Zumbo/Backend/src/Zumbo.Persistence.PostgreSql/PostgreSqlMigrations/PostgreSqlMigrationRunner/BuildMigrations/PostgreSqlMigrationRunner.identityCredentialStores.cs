using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string identityCredentialStores = """
            CREATE TABLE IF NOT EXISTS identity.refresh_sessions (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_refresh_sessions_document_gin
                ON identity.refresh_sessions USING GIN (document jsonb_path_ops);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_refresh_sessions_token_hash
                ON identity.refresh_sessions ((document #>> ARRAY['TokenHash']));
            CREATE INDEX IF NOT EXISTS ix_refresh_sessions_owner_active
                ON identity.refresh_sessions (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['UserId']),
                    (document #>> ARRAY['RevokedAtUtc']),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAtUtc']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_refresh_sessions_retain_until
                ON identity.refresh_sessions (
                    public.zumbo_parse_timestamptz(document #>> ARRAY['RetainUntilUtc']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_api_keys_owner_created
                ON identity.api_keys (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['UserId']),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['CreatedAt']) DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_api_keys_owner_revoked_expires
                ON identity.api_keys (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['UserId']),
                    (document #>> ARRAY['RevokedAtUtc']),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAtUtc']),
                    id);
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM identity.users AS users
                    CROSS JOIN LATERAL jsonb_array_elements(
                        CASE
                            WHEN jsonb_typeof(users.document -> 'RefreshTokens') = 'array'
                                THEN users.document -> 'RefreshTokens'
                            ELSE '[]'::jsonb
                        END) AS token(value)
                    JOIN identity.refresh_sessions AS existing
                      ON existing.id = COALESCE(
                          NULLIF(token.value ->> 'SessionId', ''),
                          md5(users.id || ':' || (token.value ->> 'TokenHash')))
                    WHERE NULLIF(token.value ->> 'TokenHash', '') IS NOT NULL
                      AND NULLIF(token.value ->> 'ExpiresAt', '') IS NOT NULL
                      AND (
                          existing.document ->> 'UserId' IS DISTINCT FROM users.id
                          OR existing.document ->> 'OrganizationId'
                              IS DISTINCT FROM users.document ->> 'OrganizationId'
                          OR existing.document ->> 'TokenHash'
                              IS DISTINCT FROM token.value ->> 'TokenHash'))
                THEN
                    RAISE EXCEPTION
                        'Refresh session backfill conflicts with incompatible stored ownership or token data.';
                END IF;
            END $$;
            INSERT INTO identity.refresh_sessions (id, version, document)
            SELECT
                COALESCE(NULLIF(token.value ->> 'SessionId', ''), md5(users.id || ':' || (token.value ->> 'TokenHash'))),
                1,
                jsonb_build_object(
                    'Id', COALESCE(NULLIF(token.value ->> 'SessionId', ''), md5(users.id || ':' || (token.value ->> 'TokenHash'))),
                    'UserId', users.id,
                    'OrganizationId', users.document ->> 'OrganizationId',
                    'TokenHash', token.value ->> 'TokenHash',
                    'CreatedAt', token.value -> 'CreatedAt',
                    'ExpiresAt', token.value -> 'ExpiresAt',
                    'ExpiresAtUtc', token.value -> 'ExpiresAt',
                    'RevokedAt', COALESCE(token.value -> 'RevokedAt', 'null'::jsonb),
                    'RevokedAtUtc', COALESCE(token.value -> 'RevokedAt', 'null'::jsonb),
                    'ReplacedBySessionId', 'null'::jsonb,
                    'RetainUntilUtc', to_jsonb(to_char(
                        (GREATEST(
                            public.zumbo_parse_timestamptz(token.value ->> 'ExpiresAt'),
                            COALESCE(
                                public.zumbo_parse_timestamptz(token.value ->> 'RevokedAt'),
                                public.zumbo_parse_timestamptz(token.value ->> 'ExpiresAt')))
                            + interval '30 days') AT TIME ZONE 'UTC',
                        'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')),
                    'Version', 1)
            FROM identity.users AS users
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE
                    WHEN jsonb_typeof(users.document -> 'RefreshTokens') = 'array'
                        THEN users.document -> 'RefreshTokens'
                    ELSE '[]'::jsonb
                END) AS token(value)
            WHERE NULLIF(token.value ->> 'TokenHash', '') IS NOT NULL
              AND NULLIF(token.value ->> 'ExpiresAt', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;
            DROP INDEX IF EXISTS identity.ix_users_refresh_token_hash;
            """;
}
