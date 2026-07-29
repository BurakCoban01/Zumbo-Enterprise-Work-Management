using System.Data.Common;
using System.Text.Json;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlHighCardinalityQueryPlanTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ProjectAndSessionQueries_UseBoundedAccessIndexes()
    {
        var migrations = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Contains("37:high_cardinality_indexes", migrations);

        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        await PostgreSqlFixture.ExecuteAsync(connection, """
            INSERT INTO projects.projects (id, version, document)
            SELECT
                'card-project-' || lpad(value::text, 5, '0'),
                1,
                jsonb_build_object(
                    'Id', 'card-project-' || lpad(value::text, 5, '0'),
                    'Version', 1,
                    'OrganizationId', CASE WHEN value <= 1000 THEN 'card-target-org' ELSE 'card-other-org' END,
                    'Key', 'P' || lpad(value::text, 7, '0'),
                    'Name', 'Project ' || value,
                    'Visibility', 'Internal',
                    'Archived', false,
                    'Members', jsonb_build_array())
            FROM generate_series(1, 5000) AS value;

            INSERT INTO identity.refresh_sessions (id, version, document)
            SELECT
                'card-session-' || lpad(value::text, 5, '0'),
                1,
                jsonb_build_object(
                    'Id', 'card-session-' || lpad(value::text, 5, '0'),
                    'Version', 1,
                    'OrganizationId', CASE WHEN value <= 1000 THEN 'card-target-org' ELSE 'card-other-org' END,
                    'UserId', CASE WHEN value <= 1000 THEN 'card-target-user' ELSE 'card-other-user' END,
                    'TokenHash', 'card-token-' || lpad(value::text, 5, '0'),
                    'LastSeenAt', to_char(
                        timestamptz '2026-07-01T00:00:00Z' + value * interval '1 minute',
                        'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                    'ExpiresAtUtc', '2026-08-01T00:00:00Z',
                    'RetainUntilUtc', '2026-09-01T00:00:00Z')
            FROM generate_series(1, 5000) AS value;

            ANALYZE projects.projects;
            ANALYZE identity.refresh_sessions;
            """);

        try
        {
            var projectPlan = await ScalarStringAsync(connection, """
                EXPLAIN (ANALYZE, COSTS OFF, FORMAT JSON)
                SELECT document
                FROM projects.projects
                WHERE (document #>> ARRAY['OrganizationId']) = 'card-target-org'
                  AND ((document #>> ARRAY['Archived'])::boolean) = false
                ORDER BY (document #>> ARRAY['Key']), id COLLATE "C"
                LIMIT 100;
                """);
            AssertPlan(projectPlan, "ix_projects_organization_archived_key_cursor");
            Assert.DoesNotContain("Sort", projectPlan, StringComparison.Ordinal);

            var sessionPlan = await ScalarStringAsync(connection, """
                EXPLAIN (ANALYZE, COSTS OFF, FORMAT JSON)
                SELECT document
                FROM identity.refresh_sessions
                WHERE (document #>> ARRAY['OrganizationId']) = 'card-target-org'
                  AND (document #>> ARRAY['UserId']) = 'card-target-user'
                ORDER BY public.zumbo_parse_timestamptz(
                    document #>> ARRAY['LastSeenAt']) DESC NULLS LAST,
                    id COLLATE "C"
                LIMIT 100;
                """);
            AssertPlan(sessionPlan, "ix_refresh_sessions_owner_last_seen");
            AssertBoundedOptionalTopNSort(sessionPlan);
        }
        finally
        {
            await PostgreSqlFixture.ExecuteAsync(
                connection,
                "DELETE FROM identity.refresh_sessions WHERE id LIKE 'card-session-%';");
            await PostgreSqlFixture.ExecuteAsync(
                connection,
                "DELETE FROM projects.projects WHERE id LIKE 'card-project-%';");
        }
    }

    private static void AssertPlan(string planJson, string indexName)
    {
        using var document = JsonDocument.Parse(planJson);
        Assert.Contains(indexName, planJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan", planJson, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(
            document.RootElement[0].GetProperty("Execution Time").GetDouble(),
            0,
            250);
    }

    private static void AssertBoundedOptionalTopNSort(string planJson)
    {
        using var document = JsonDocument.Parse(planJson);
        var sort = FindPlanNode(document.RootElement[0].GetProperty("Plan"), "Sort");
        if (sort is null)
        {
            return;
        }

        Assert.Equal("top-N heapsort", sort.Value.GetProperty("Sort Method").GetString());
        Assert.InRange(sort.Value.GetProperty("Sort Space Used").GetInt64(), 0, 256);
        var source = sort.Value.GetProperty("Plans")[0];
        Assert.InRange(source.GetProperty("Actual Rows").GetInt64(), 1, 1_000);
    }

    private static JsonElement? FindPlanNode(JsonElement node, string nodeType)
    {
        if (node.GetProperty("Node Type").GetString() == nodeType)
        {
            return node.Clone();
        }

        if (!node.TryGetProperty("Plans", out var plans))
        {
            return null;
        }

        foreach (var child in plans.EnumerateArray())
        {
            var match = FindPlanNode(child, nodeType);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static async Task<string> ScalarStringAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }
}
