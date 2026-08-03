using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Zumbo.BuildingBlocks.Application.Messaging;

namespace Zumbo.Persistence.PostgreSql;

public sealed class PostgreSqlDurableEventInbox(
    PostgreSqlSession session,
    PostgreSqlPersistenceOptions options) : IDurableEventInbox
{
    public async Task<bool> HasProcessedAsync(
        string consumerName,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM messaging.inbox_messages WHERE consumer_name=@consumer AND message_id=@messageId);";
        await using var lease = await session.LeaseAsync(cancellationToken);
        await using var command = lease.CreateCommand(sql, options.CommandTimeoutSeconds);
        AddKey(command, consumerName, messageId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> MarkProcessedAsync(
        string consumerName,
        string messageId,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO messaging.inbox_messages (consumer_name, message_id, processed_at_utc)
            VALUES (@consumer, @messageId, @processedAt)
            ON CONFLICT DO NOTHING;
            """;
        await using var lease = await session.LeaseAsync(cancellationToken);
        await using var command = lease.CreateCommand(sql, options.CommandTimeoutSeconds);
        AddKey(command, consumerName, messageId);
        command.Parameters.AddWithValue("processedAt", processedAtUtc);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static void AddKey(NpgsqlCommand command, string consumerName, string messageId)
    {
        if (string.IsNullOrWhiteSpace(consumerName) || string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("Inbox consumer and message identifiers are required.");
        }
        command.Parameters.AddWithValue("consumer", consumerName.Trim());
        command.Parameters.AddWithValue("messageId", messageId.Trim());
    }
}
