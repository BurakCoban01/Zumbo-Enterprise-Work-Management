namespace Zumbo.Persistence.PostgreSql;

internal static class V013ExpireLegacyTeamInvitesMigration
{
        private const string UpSql = """
            UPDATE teams.teams team
            SET version = team.version + 1,
                document = jsonb_set(
                    team.document,
                    '{Members}',
                    COALESCE((
                        SELECT jsonb_agg(
                            CASE
                                WHEN member.value ->> 'Status' = 'Invited'
                                  AND COALESCE(member.value ->> 'InvitationTokenHash', '') = ''
                                THEN member.value || jsonb_build_object(
                                    'Status', 'Expired',
                                    'InvitationTokenHash', NULL,
                                    'InvitationExpiresAt', NULL,
                                    'RespondedAt', transaction_timestamp())
                                ELSE member.value
                            END
                            ORDER BY member.ordinality)
                        FROM jsonb_array_elements(COALESCE(team.document -> 'Members', '[]'::jsonb))
                            WITH ORDINALITY AS member(value, ordinality)),
                        '[]'::jsonb),
                    true)
                    || jsonb_build_object(
                        'Version', team.version + 1,
                        'TeamInviteTokenMigratedBy', '20260720_013'),
                updated_at = transaction_timestamp()
            WHERE EXISTS (
                SELECT 1
                FROM jsonb_array_elements(COALESCE(team.document -> 'Members', '[]'::jsonb)) member
                WHERE member ->> 'Status' = 'Invited'
                  AND COALESCE(member ->> 'InvitationTokenHash', '') = '');
            """;

        private const string DownSql = """
            UPDATE teams.teams
            SET version = version + 1,
                document = (document - 'TeamInviteTokenMigratedBy')
                    || jsonb_build_object('Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE document ->> 'TeamInviteTokenMigratedBy' = '20260720_013';
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        13,
        "expire_legacy_team_invites",
        UpSql,
        DownSql);
}
