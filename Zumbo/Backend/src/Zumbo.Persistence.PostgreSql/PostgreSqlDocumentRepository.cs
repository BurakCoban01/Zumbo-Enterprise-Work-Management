using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

public sealed class PostgreSqlDocumentRepository<TDocument>(
    PostgreSqlSession session,
    PostgreSqlPersistenceOptions options) : IPostgreSqlRepository<TDocument>
    where TDocument : class, IDocument
{
    private readonly PostgreSqlDocumentStorage storage = options.Resolve(typeof(TDocument));

    public async Task<TDocument> CreateAsync(
        TDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            document.Id = Guid.NewGuid().ToString("N");
        }

        if (document is IVersionedDocument versioned && versioned.Version <= 0)
        {
            versioned.Version = 1;
        }

        var snapshot = Clone(document);
        const string operation = "INSERT INTO {0} (id, version, document) VALUES (@id, @version, @document);";
        await using var lease = await session.LeaseAsync(cancellationToken);
        await using var command = lease.CreateCommand(string.Format(operation, Qualified), options.CommandTimeoutSeconds);
        AddDocumentParameters(command, snapshot);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsConstraintConflict(exception))
        {
            throw new DocumentConflictException(
                $"Creating {typeof(TDocument).Name} '{snapshot.Id}' conflicts with an existing document.",
                exception);
        }

        return Clone(snapshot);
    }

    public async Task<TDocument?> SelectAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        var translator = new PostgreSqlExpressionTranslator(options.JsonSerializerOptions);
        var predicate = translator.TranslatePredicate(filter);
        var sql = $"SELECT document FROM {Qualified} WHERE {predicate} ORDER BY id COLLATE \"C\" LIMIT 1;";
        await using var command = await CreateCommandAsync(sql, translator, cancellationToken);
        return await ReadSingleAsync(command.Command, cancellationToken);
    }

    public async Task<IReadOnlyList<TDocument>> ListByFilterAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        Expression<Func<TDocument, object>>? orderBy = null,
        bool orderDescending = false,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, 200);
        var offset = Math.Min((long)(safePage - 1) * safeSize, int.MaxValue);
        var translator = new PostgreSqlExpressionTranslator(options.JsonSerializerOptions);
        var predicate = filter is null ? "TRUE" : translator.TranslatePredicate(filter);
        var direction = orderDescending ? "DESC" : "ASC";
        var order = orderBy is null
            ? "id COLLATE \"C\" ASC"
            : $"{translator.TranslateOrder(orderBy)} {direction} NULLS LAST, id COLLATE \"C\" ASC";
        var sql = $"SELECT document FROM {Qualified} WHERE {predicate} ORDER BY {order} OFFSET @offset LIMIT @limit;";
        await using var command = await CreateCommandAsync(sql, translator, cancellationToken);
        command.Parameters.AddWithValue("offset", NpgsqlDbType.Integer, (int)offset);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, safeSize);
        return await ReadManyAsync(command.Command, cancellationToken);
    }

    public async Task<DocumentCursorPage<TDocument>> ListByCursorAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        string? afterId = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var safeSize = Math.Clamp(pageSize, 1, 200);
        var translator = new PostgreSqlExpressionTranslator(options.JsonSerializerOptions);
        var predicate = filter is null ? "TRUE" : translator.TranslatePredicate(filter);
        var cursor = afterId is null ? string.Empty : " AND id COLLATE \"C\" > @after COLLATE \"C\"";
        var sql = $"SELECT document FROM {Qualified} WHERE ({predicate}){cursor} ORDER BY id COLLATE \"C\" LIMIT @limit;";
        await using var command = await CreateCommandAsync(sql, translator, cancellationToken);
        if (afterId is not null) command.Parameters.AddWithValue("after", afterId);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, safeSize + 1);
        var candidates = await ReadManyAsync(command.Command, cancellationToken);
        var items = candidates.Take(safeSize).ToList();
        return new DocumentCursorPage<TDocument>(items, candidates.Count > safeSize ? items[^1].Id : null);
    }

    public async Task<long> CountByFilterAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        CancellationToken cancellationToken = default)
    {
        var translator = new PostgreSqlExpressionTranslator(options.JsonSerializerOptions);
        var predicate = filter is null ? "TRUE" : translator.TranslatePredicate(filter);
        await using var command = await CreateCommandAsync(
            $"SELECT count(*) FROM {Qualified} WHERE {predicate};",
            translator,
            cancellationToken);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async Task<bool> ExistsByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        var translator = new PostgreSqlExpressionTranslator(options.JsonSerializerOptions);
        var predicate = translator.TranslatePredicate(filter);
        await using var command = await CreateCommandAsync(
            $"SELECT EXISTS (SELECT 1 FROM {Qualified} WHERE {predicate});",
            translator,
            cancellationToken);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<DocumentMutationResult> ReplaceByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        CancellationToken cancellationToken = default)
    {
        var current = await SelectAsync(filter, cancellationToken);
        if (current is null) return new DocumentMutationResult(0, 0);

        var snapshot = Clone(replacement);
        snapshot.Id = current.Id;
        if (JsonEqual(current, snapshot)) return new DocumentMutationResult(1, 0);

        var translator = new PostgreSqlExpressionTranslator(options.JsonSerializerOptions);
        var predicate = translator.TranslatePredicate(filter);
        var sql = $"UPDATE {Qualified} SET document=@document, version=@version, updated_at=transaction_timestamp() " +
                  $"WHERE id=@id AND ({predicate});";
        await using var command = await CreateCommandAsync(sql, translator, cancellationToken);
        AddDocumentParameters(command.Command, snapshot);
        try
        {
            var changed = await command.ExecuteNonQueryAsync(cancellationToken);
            return new DocumentMutationResult(changed, changed);
        }
        catch (PostgresException exception) when (IsConstraintConflict(exception))
        {
            throw new DocumentConflictException(
                $"Replacing {typeof(TDocument).Name} '{current.Id}' conflicts with an existing document.",
                exception);
        }
    }

    public async Task<DocumentCompareExchangeResult> ReplaceByVersionAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (!typeof(IVersionedDocument).IsAssignableFrom(typeof(TDocument)) || expectedVersion <= 0)
        {
            throw new DocumentQueryException(
                $"{typeof(TDocument).Name} must implement IVersionedDocument and expected version must be positive.");
        }

        var current = await SelectAsync(filter, cancellationToken);
        if (current is null) return new DocumentCompareExchangeResult(0, 0, null);
        var actual = ((IVersionedDocument)current).Version;
        if (actual != expectedVersion)
        {
            throw new DocumentConcurrencyException(current.Id, expectedVersion, actual);
        }

        var next = checked(expectedVersion + 1);
        var snapshot = Clone(replacement);
        snapshot.Id = current.Id;
        ((IVersionedDocument)snapshot).Version = next;
        var translator = new PostgreSqlExpressionTranslator(options.JsonSerializerOptions);
        var predicate = translator.TranslatePredicate(filter);
        var sql = $"UPDATE {Qualified} SET document=@document, version=@next, updated_at=transaction_timestamp() " +
                  $"WHERE id=@id AND version=@expected AND ({predicate});";
        await using var command = await CreateCommandAsync(sql, translator, cancellationToken);
        command.Parameters.AddWithValue("id", snapshot.Id);
        command.Parameters.AddWithValue("expected", expectedVersion);
        command.Parameters.AddWithValue("next", next);
        command.Parameters.Add(new NpgsqlParameter("document", NpgsqlDbType.Jsonb) { Value = Serialize(snapshot) });
        try
        {
            var changed = await command.ExecuteNonQueryAsync(cancellationToken);
            if (changed == 1) return new DocumentCompareExchangeResult(1, 1, next);
        }
        catch (PostgresException exception) when (IsConstraintConflict(exception))
        {
            throw new DocumentConflictException(
                $"Replacing {typeof(TDocument).Name} '{current.Id}' conflicts with an existing document.",
                exception);
        }

        var latest = await SelectByIdAsync(current.Id, cancellationToken);
        if (latest is null) return new DocumentCompareExchangeResult(0, 0, null);
        throw new DocumentConcurrencyException(
            current.Id,
            expectedVersion,
            ((IVersionedDocument)latest).Version);
    }

    public async Task<long> DeleteByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        var translator = new PostgreSqlExpressionTranslator(options.JsonSerializerOptions);
        var predicate = translator.TranslatePredicate(filter);
        await using var command = await CreateCommandAsync(
            $"DELETE FROM {Qualified} WHERE {predicate};",
            translator,
            cancellationToken);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DocumentMutationResult> UpdateOneFieldByFilterAsync<TField>(
        Expression<Func<TDocument, bool>> filter,
        Expression<Func<TDocument, TField>> field,
        TField value,
        CancellationToken cancellationToken = default)
    {
        var property = DirectProperty(field);
        var current = await SelectAsync(filter, cancellationToken);
        if (current is null) return new DocumentMutationResult(0, 0);
        var snapshot = Clone(current);
        if (Equals(property.GetValue(snapshot), value)) return new DocumentMutationResult(1, 0);
        property.SetValue(snapshot, value);

        var translator = new PostgreSqlExpressionTranslator(options.JsonSerializerOptions);
        var predicate = translator.TranslatePredicate(filter);
        var sql = $"UPDATE {Qualified} SET document=@document, version=@version, updated_at=transaction_timestamp() " +
                  $"WHERE id=@id AND ({predicate});";
        await using var command = await CreateCommandAsync(sql, translator, cancellationToken);
        AddDocumentParameters(command.Command, snapshot);
        try
        {
            var changed = await command.ExecuteNonQueryAsync(cancellationToken);
            return new DocumentMutationResult(changed, changed);
        }
        catch (PostgresException exception) when (IsConstraintConflict(exception))
        {
            throw new DocumentConflictException(
                $"Updating {typeof(TDocument).Name} '{current.Id}' conflicts with an existing document.",
                exception);
        }
    }

    private string Qualified => $"{SqlIdentifier.Quote(storage.Schema)}.{SqlIdentifier.Quote(storage.Table)}";

    private async Task<PostgreSqlOwnedCommand> CreateCommandAsync(
        string sql,
        PostgreSqlExpressionTranslator translator,
        CancellationToken cancellationToken)
    {
        var lease = await session.LeaseAsync(cancellationToken);
        var command = lease.CreateCommand(sql, options.CommandTimeoutSeconds);
        translator.AddParameters(command);
        return new PostgreSqlOwnedCommand(command, lease);
    }

    private async Task<TDocument?> SelectByIdAsync(string id, CancellationToken cancellationToken)
    {
        await using var lease = await session.LeaseAsync(cancellationToken);
        await using var command = lease.CreateCommand(
            $"SELECT document FROM {Qualified} WHERE id=@id;",
            options.CommandTimeoutSeconds);
        command.Parameters.AddWithValue("id", id);
        return await ReadSingleAsync(command, cancellationToken);
    }

    private async Task<TDocument?> ReadSingleAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Deserialize(value.ToString()!);
    }

    private async Task<IReadOnlyList<TDocument>> ReadManyAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<TDocument>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Deserialize(reader.GetString(0)));
        return result;
    }

    private void AddDocumentParameters(NpgsqlCommand command, TDocument document)
    {
        command.Parameters.AddWithValue("id", document.Id);
        command.Parameters.AddWithValue("version", ReadVersion(document));
        command.Parameters.Add(new NpgsqlParameter("document", NpgsqlDbType.Jsonb) { Value = Serialize(document) });
    }

    private string Serialize(TDocument document) => JsonSerializer.Serialize(document, options.JsonSerializerOptions);
    private TDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<TDocument>(json, options.JsonSerializerOptions)
        ?? throw new InvalidOperationException($"Stored {typeof(TDocument).Name} JSON was null.");
    private TDocument Clone(TDocument document) => Deserialize(Serialize(document));
    private bool JsonEqual(TDocument left, TDocument right) => Serialize(left) == Serialize(right);
    private static long ReadVersion(TDocument document) => document is IVersionedDocument versioned ? versioned.Version : 0;

    private static PropertyInfo DirectProperty<TField>(Expression<Func<TDocument, TField>> selector)
    {
        Expression body = selector.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        if (body is not MemberExpression { Expression: ParameterExpression } member
            || member.Member is not PropertyInfo { CanWrite: true } property)
        {
            throw new DocumentQueryException("Field selector must target one direct writable property.");
        }

        return property;
    }

    private static bool IsConstraintConflict(PostgresException exception) =>
        exception.SqlState is PostgresErrorCodes.UniqueViolation
            or PostgresErrorCodes.CheckViolation
            or PostgresErrorCodes.ForeignKeyViolation;
}

internal sealed class PostgreSqlOwnedCommand(
    NpgsqlCommand command,
    PostgreSqlConnectionLease lease) : IAsyncDisposable
{
    public NpgsqlCommand Command => command;
    public NpgsqlParameterCollection Parameters => command.Parameters;
    public Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => command.ExecuteNonQueryAsync(cancellationToken);
    public Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) => command.ExecuteScalarAsync(cancellationToken);
    public Task<NpgsqlDataReader> ExecuteReaderAsync(CancellationToken cancellationToken) => command.ExecuteReaderAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await command.DisposeAsync();
        await lease.DisposeAsync();
    }
}
