using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Zumbo.BuildingBlocks.Application.Messaging;

namespace Zumbo.Persistence.PostgreSql;

public sealed class PostgreSqlDurableEventOutbox(
    PostgreSqlSession session,
    PostgreSqlPersistenceOptions options,
    ILogger<PostgreSqlDurableEventOutbox>? logger = null) : IDurableEventOutbox
{
    public async Task EnqueueAsync(
        DurableEventEnvelope message,
        CancellationToken cancellationToken = default)
    {
        if (!session.HasActiveTransaction)
        {
            throw new InvalidOperationException("Durable events must be enqueued inside an active PostgreSQL transaction.");
        }

        const string sql = """
            INSERT INTO messaging.outbox_messages (
                id, owner_module, event_type, schema_version, tenant_id, correlation_id,
                deduplication_key, payload, occurred_at_utc, available_at_utc)
            VALUES (
                @id, @owner, @type, @schemaVersion, @tenant, @correlation,
                @deduplicationKey, @payload, @occurredAt, @occurredAt)
            ON CONFLICT DO NOTHING;
            """;
        await using var lease = await session.LeaseAsync(cancellationToken);
        await using var command = lease.CreateCommand(sql, options.CommandTimeoutSeconds);
        AddEnvelopeParameters(command, message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DurableEventLease>> ClaimAsync(
        string workerId,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateClaim(workerId, batchSize, leaseDuration);
        const string sql = """
            WITH candidates AS (
                SELECT id
                FROM messaging.outbox_messages
                WHERE (status = 'Pending' AND available_at_utc <= @now)
                   OR (status = 'Processing' AND lease_until_utc <= @now)
                ORDER BY occurred_at_utc, id
                FOR UPDATE SKIP LOCKED
                LIMIT @batchSize
            )
            UPDATE messaging.outbox_messages AS message
            SET status = 'Processing',
                attempt_count = attempt_count + 1,
                lease_owner = @workerId,
                lease_token = @leaseToken,
                lease_until_utc = @leaseUntil,
                updated_at_utc = @now
            FROM candidates
            WHERE message.id = candidates.id
            RETURNING message.id, message.owner_module, message.event_type,
                message.schema_version, message.tenant_id, message.correlation_id,
                message.deduplication_key, message.payload::text, message.occurred_at_utc,
                message.attempt_count, message.lease_token, message.lease_until_utc;
            """;
        var leaseToken = Guid.NewGuid().ToString("N");

        await using var lease = await session.LeaseAsync(cancellationToken);
        if (lease.Transaction is not null)
        {
            return await ClaimWithCommandAsync(
                lease.CreateCommand(sql, options.CommandTimeoutSeconds),
                workerId,
                leaseToken,
                batchSize,
                nowUtc,
                nowUtc.Add(leaseDuration),
                cancellationToken);
        }

        await using var transaction = await lease.Connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = lease.CreateCommand(sql, options.CommandTimeoutSeconds);
            command.Transaction = transaction;
            var claimed = await ClaimWithCommandAsync(
                command,
                workerId,
                leaseToken,
                batchSize,
                nowUtc,
                nowUtc.Add(leaseDuration),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return claimed;
        }
        catch
        {
            await PostgreSqlCompensation.RunAsync(
                "postgres.outbox_claim.rollback",
                token => transaction.RollbackAsync(token),
                logger);
            throw;
        }
    }

    public Task<bool> CompleteAsync(
        string messageId,
        string leaseToken,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default) =>
        UpdateStateAsync(
            """
            UPDATE messaging.outbox_messages
            SET status='Completed', completed_at_utc=@now, lease_owner=NULL, lease_token=NULL,
                lease_until_utc=NULL, last_error=NULL, updated_at_utc=@now
            WHERE id=@id AND status='Processing' AND lease_token=@leaseToken;
            """,
            messageId,
            leaseToken,
            completedAtUtc,
            cancellationToken);

    public async Task<DurableMessageFailure> FailAsync(
        string messageId,
        string leaseToken,
        string error,
        int maximumAttempts,
        DateTimeOffset nowUtc,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (maximumAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        const string sql = """
            UPDATE messaging.outbox_messages
            SET status = CASE WHEN attempt_count >= @maximumAttempts THEN 'DeadLetter' ELSE 'Pending' END,
                available_at_utc = CASE WHEN attempt_count >= @maximumAttempts THEN available_at_utc ELSE @nextAttempt END,
                dead_lettered_at_utc = CASE WHEN attempt_count >= @maximumAttempts THEN @now ELSE NULL END,
                lease_owner = NULL,
                lease_token = NULL,
                lease_until_utc = NULL,
                last_error = @error,
                updated_at_utc = @now
            WHERE id=@id AND status='Processing' AND lease_token=@leaseToken
            RETURNING attempt_count, status;
            """;
        await using var lease = await session.LeaseAsync(cancellationToken);
        await using var command = lease.CreateCommand(sql, options.CommandTimeoutSeconds);
        command.Parameters.AddWithValue("id", Required(messageId));
        command.Parameters.AddWithValue("leaseToken", Required(leaseToken));
        command.Parameters.AddWithValue("maximumAttempts", maximumAttempts);
        command.Parameters.AddWithValue("nextAttempt", nextAttemptAtUtc);
        command.Parameters.AddWithValue("now", nowUtc);
        command.Parameters.AddWithValue("error", Truncate(error, 4000));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new DurableMessageFailure(false, false, 0, null);
        }

        var attempt = reader.GetInt32(0);
        var deadLettered = reader.GetString(1) == DurableMessageStates.DeadLetter;
        return new DurableMessageFailure(true, deadLettered, attempt, deadLettered ? null : nextAttemptAtUtc);
    }

    public Task<bool> ReplayDeadLetterAsync(
        string messageId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        UpdateStateAsync(
            """
            UPDATE messaging.outbox_messages
            SET status='Pending', attempt_count=0, available_at_utc=@now,
                lease_owner=NULL, lease_token=NULL, lease_until_utc=NULL, last_error=NULL,
                dead_lettered_at_utc=NULL, completed_at_utc=NULL, updated_at_utc=@now
            WHERE id=@id AND status='DeadLetter';
            """,
            messageId,
            leaseToken: null,
            nowUtc,
            cancellationToken);

    public async Task<IReadOnlyList<DurableDeadLetterSummary>> ListDeadLettersAsync(
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        const string sql = """
            SELECT id, event_type, attempt_count, dead_lettered_at_utc
            FROM messaging.outbox_messages
            WHERE status='DeadLetter'
            ORDER BY dead_lettered_at_utc DESC, id
            LIMIT @pageSize;
            """;
        await using var lease = await session.LeaseAsync(cancellationToken);
        await using var command = lease.CreateCommand(sql, options.CommandTimeoutSeconds);
        command.Parameters.AddWithValue("pageSize", pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<DurableDeadLetterSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new DurableDeadLetterSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return items;
    }

    public async Task<DurableOutboxMetrics> GetMetricsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                count(*) FILTER (WHERE status='Pending'),
                count(*) FILTER (WHERE status='Processing'),
                count(*) FILTER (WHERE status='DeadLetter'),
                count(*) FILTER (WHERE status='Completed'),
                count(*) FILTER (WHERE attempt_count > 1),
                min(occurred_at_utc) FILTER (WHERE status='Pending')
            FROM messaging.outbox_messages;
            """;
        await using var lease = await session.LeaseAsync(cancellationToken);
        await using var command = lease.CreateCommand(sql, options.CommandTimeoutSeconds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new DurableOutboxMetrics(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            nowUtc);
    }

    private async Task<IReadOnlyList<DurableEventLease>> ClaimWithCommandAsync(
        NpgsqlCommand command,
        string workerId,
        string leaseToken,
        int batchSize,
        DateTimeOffset nowUtc,
        DateTimeOffset leaseUntilUtc,
        CancellationToken cancellationToken)
    {
        command.Parameters.AddWithValue("workerId", workerId);
        command.Parameters.AddWithValue("leaseToken", leaseToken);
        command.Parameters.AddWithValue("batchSize", batchSize);
        command.Parameters.AddWithValue("now", nowUtc);
        command.Parameters.AddWithValue("leaseUntil", leaseUntilUtc);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<DurableEventLease>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var envelope = new DurableEventEnvelope(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8));
            result.Add(new DurableEventLease(
                envelope,
                reader.GetInt32(9),
                workerId,
                reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11)));
        }

        return result;
    }

    private async Task<bool> UpdateStateAsync(
        string sql,
        string messageId,
        string? leaseToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var lease = await session.LeaseAsync(cancellationToken);
        await using var command = lease.CreateCommand(sql, options.CommandTimeoutSeconds);
        command.Parameters.AddWithValue("id", Required(messageId));
        if (leaseToken is not null)
        {
            command.Parameters.AddWithValue("leaseToken", Required(leaseToken));
        }
        command.Parameters.AddWithValue("now", nowUtc);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static void AddEnvelopeParameters(NpgsqlCommand command, DurableEventEnvelope message)
    {
        command.Parameters.AddWithValue("id", message.Id);
        command.Parameters.AddWithValue("owner", message.OwnerModule);
        command.Parameters.AddWithValue("type", message.EventType);
        command.Parameters.AddWithValue("schemaVersion", message.SchemaVersion);
        command.Parameters.AddWithValue("tenant", message.TenantId);
        command.Parameters.AddWithValue("correlation", message.CorrelationId);
        command.Parameters.AddWithValue("deduplicationKey", (object?)message.DeduplicationKey ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = message.Payload });
        command.Parameters.AddWithValue("occurredAt", message.OccurredAtUtc);
    }

    private static void ValidateClaim(string workerId, int batchSize, TimeSpan leaseDuration)
    {
        _ = Required(workerId);
        if (batchSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(15))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }
    }

    private static string Required(string value) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("Value cannot be empty.");

    private static string Truncate(string value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown durable event failure." : value.Trim()[..Math.Min(value.Trim().Length, maximumLength)];
}
