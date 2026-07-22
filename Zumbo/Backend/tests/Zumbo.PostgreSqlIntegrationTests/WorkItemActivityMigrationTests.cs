using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class WorkItemActivityMigrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ActivityMigration_BackfillsLegacyDataAndRollbackReembedsIt()
    {
        var latest = Assert.Single(
            await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None),
            x => x.StartsWith("11:", StringComparison.Ordinal));
        var suffix = Guid.NewGuid().ToString("N");
        var projectId = "data007-project-" + suffix;
        var workItemId = "data007-item-" + suffix;
        var commentId = "data007-comment-" + suffix;
        var now = DateTimeOffset.UtcNow;

        await fixture.Api.RollbackAsync(latest, CancellationToken.None);
        try
        {
            await using (var connection = (NpgsqlConnection)await fixture.Api.OpenConnectionAsync(CancellationToken.None))
            {
                await InsertDocumentAsync(connection, "projects.projects", projectId, new
                {
                    Id = projectId,
                    OrganizationId = "org-data007",
                    Version = 0
                });
                await InsertDocumentAsync(connection, "work_items.work_items", workItemId, new
                {
                    Id = workItemId,
                    ProjectId = projectId,
                    BoardId = "board-data007",
                    ActivityStorageVersion = 0,
                    Comments = new[]
                    {
                        new
                        {
                            Id = commentId,
                            Body = "current",
                            AuthorUserId = "user-a",
                            Mentions = new[] { "user-b" },
                            CreatedAt = now,
                            EditedAt = (DateTimeOffset?)now.AddMinutes(1),
                            History = new[]
                            {
                                new { Body = "old", EditedByUserId = "user-a", EditedAt = now }
                            }
                        }
                    },
                    Attachments = new[]
                    {
                        new { Id = "attachment-" + suffix, FileName = "a.txt", ContentType = "text/plain", SizeBytes = 1, StoragePath = "a", ChecksumSha256 = "a", CreatedAt = now }
                    },
                    WorkLogs = new[]
                    {
                        new { Id = "log-" + suffix, UserId = "user-a", Hours = 1.5m, Note = (string?)null, CreatedAt = now }
                    },
                    Approvals = new[]
                    {
                        new { Id = "approval-" + suffix, FromStatus = "To Do", ToStatus = "Done", RequestedByUserId = "user-a", RequestedAt = now, ExpiresAt = now.AddDays(1), Status = "Pending" }
                    },
                    StatusHistory = new[]
                    {
                        new { FromStatus = (string?)null, ToStatus = "To Do", ChangedByUserId = "user-a", ChangedAt = now }
                    },
                    Version = 0
                });
            }

            await fixture.Api.MigrateAsync(CancellationToken.None);
            await using (var connection = (NpgsqlConnection)await fixture.Api.OpenConnectionAsync(CancellationToken.None))
            {
                Assert.Equal(1, await CountAsync(connection, "work_items.work_item_comments", workItemId));
                Assert.Equal(1, await CountAsync(connection, "work_items.work_item_comment_revisions", workItemId));
                Assert.Equal(1, await CountAsync(connection, "work_items.work_item_attachments", workItemId));
                Assert.Equal(1, await CountAsync(connection, "work_items.work_item_work_logs", workItemId));
                Assert.Equal(1, await CountAsync(connection, "work_items.work_item_approvals", workItemId));
                Assert.Equal(1, await CountAsync(connection, "work_items.work_item_timeline", workItemId));
                var separated = await ReadDocumentAsync(connection, "work_items.work_items", workItemId);
                Assert.Equal(1, separated.RootElement.GetProperty("ActivityStorageVersion").GetInt32());
                Assert.Empty(separated.RootElement.GetProperty("Comments").EnumerateArray());
            }

            await fixture.Api.RollbackAsync(latest, CancellationToken.None);
            await using (var connection = (NpgsqlConnection)await fixture.Api.OpenConnectionAsync(CancellationToken.None))
            {
                var restored = await ReadDocumentAsync(connection, "work_items.work_items", workItemId);
                Assert.Equal(0, restored.RootElement.GetProperty("ActivityStorageVersion").GetInt32());
                var comment = Assert.Single(restored.RootElement.GetProperty("Comments").EnumerateArray());
                Assert.Equal(commentId, comment.GetProperty("Id").GetString());
                Assert.Single(comment.GetProperty("History").EnumerateArray());
            }
        }
        finally
        {
            if (!(await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None))
                .Any(x => x.StartsWith("11:", StringComparison.Ordinal)))
            {
                await fixture.Api.MigrateAsync(CancellationToken.None);
            }

            await using var connection = (NpgsqlConnection)await fixture.Api.OpenConnectionAsync(CancellationToken.None);
            foreach (var table in new[]
            {
                "work_items.work_item_comment_revisions", "work_items.work_item_comments",
                "work_items.work_item_attachments", "work_items.work_item_work_logs",
                "work_items.work_item_approvals", "work_items.work_item_timeline"
            })
            {
                await DeleteOwnedAsync(connection, table, workItemId);
            }
            await DeleteByIdAsync(connection, "work_items.work_items", workItemId);
            await DeleteByIdAsync(connection, "projects.projects", projectId);
        }
    }

    private static async Task InsertDocumentAsync(NpgsqlConnection connection, string table, string id, object document)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table} (id, version, document) VALUES (@id, 0, @document);";
        command.Parameters.AddWithValue("id", id);
        command.Parameters.Add(new NpgsqlParameter("document", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(document)
        });
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string table, string workItemId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table} WHERE document ->> 'WorkItemId' = @id;";
        command.Parameters.AddWithValue("id", workItemId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<JsonDocument> ReadDocumentAsync(NpgsqlConnection connection, string table, string id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT document::text FROM {table} WHERE id = @id;";
        command.Parameters.AddWithValue("id", id);
        return JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
    }

    private static async Task DeleteOwnedAsync(NpgsqlConnection connection, string table, string workItemId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE document ->> 'WorkItemId' = @id;";
        command.Parameters.AddWithValue("id", workItemId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteByIdAsync(NpgsqlConnection connection, string table, string id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE id = @id;";
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }
}
