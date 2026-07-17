using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

// Temporary source compatibility while callers migrate to the inward application port.
public interface IDocument : ApplicationPersistence.IDocument;

public interface IDocumentRepository<TDocument> : ApplicationPersistence.IDocumentRepository<TDocument>
    where TDocument : class, ApplicationPersistence.IDocument;

public interface IMongoRepository<TDocument> : IDocumentRepository<TDocument>
    where TDocument : class, ApplicationPersistence.IDocument;

public sealed class InMemoryDocumentRepository<TDocument> : IDocumentRepository<TDocument>
    where TDocument : class, ApplicationPersistence.IDocument
{
    private readonly ConcurrentDictionary<string, TDocument> _documents = new();

    public Task<TDocument> CreateAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(document.Id))
        {
            document.Id = Guid.NewGuid().ToString("N");
        }

        DocumentVersion.Initialize(document);

        var snapshot = DocumentSnapshot.Clone(document);
        if (!_documents.TryAdd(snapshot.Id, snapshot))
        {
            throw new ApplicationPersistence.DocumentConflictException(
                $"A {typeof(TDocument).Name} document with id '{snapshot.Id}' already exists.");
        }

        return Task.FromResult(DocumentSnapshot.Clone(snapshot));
    }

    public Task<TDocument?> SelectAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = filter.Compile();
        var selected = CancellationAware.Enumerate(_documents.Values, cancellationToken)
            .Where(predicate)
            .OrderBy(document => document.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        return Task.FromResult(selected is null ? null : DocumentSnapshot.Clone(selected));
    }

    public Task<IReadOnlyList<TDocument>> ListByFilterAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        Expression<Func<TDocument, object>>? orderBy = null,
        bool orderDescending = false,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var skip = (int)Math.Min((long)(safePage - 1) * safePageSize, int.MaxValue);
        var predicate = (filter ?? (_ => true)).Compile();

        IEnumerable<TDocument> query = CancellationAware
            .Enumerate(_documents.Values, cancellationToken)
            .Where(predicate);

        if (orderBy is not null)
        {
            var keySelector = orderBy.Compile();
            query = orderDescending
                ? query.OrderByDescending(keySelector).ThenBy(document => document.Id, StringComparer.Ordinal)
                : query.OrderBy(keySelector).ThenBy(document => document.Id, StringComparer.Ordinal);
        }
        else
        {
            query = query.OrderBy(document => document.Id, StringComparer.Ordinal);
        }

        IReadOnlyList<TDocument> result = query
            .Skip(skip)
            .Take(safePageSize)
            .Select(DocumentSnapshot.Clone)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<ApplicationPersistence.DocumentCursorPage<TDocument>> ListByCursorAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        string? afterId = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var predicate = (filter ?? (_ => true)).Compile();

        var candidates = CancellationAware.Enumerate(_documents.Values, cancellationToken)
            .Where(predicate)
            .Where(document => afterId is null || string.CompareOrdinal(document.Id, afterId) > 0)
            .OrderBy(document => document.Id, StringComparer.Ordinal)
            .Take(safePageSize + 1)
            .ToList();
        var hasMore = candidates.Count > safePageSize;
        var items = candidates
            .Take(safePageSize)
            .Select(DocumentSnapshot.Clone)
            .ToList();

        return Task.FromResult(new ApplicationPersistence.DocumentCursorPage<TDocument>(
            items,
            hasMore ? items[^1].Id : null));
    }

    public Task<long> CountByFilterAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = (filter ?? (_ => true)).Compile();
        return Task.FromResult(CancellationAware
            .Enumerate(_documents.Values, cancellationToken)
            .LongCount(predicate));
    }

    public Task<bool> ExistsByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CancellationAware
            .Enumerate(_documents.Values, cancellationToken)
            .Any(filter.Compile()));
    }

    public Task<ApplicationPersistence.DocumentMutationResult> ReplaceByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = filter.Compile();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = CancellationAware.Enumerate(_documents.Values, cancellationToken)
                .Where(predicate)
                .OrderBy(document => document.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            if (existing is null)
            {
                return Task.FromResult(new ApplicationPersistence.DocumentMutationResult(0, 0));
            }

            var replacementSnapshot = DocumentSnapshot.Clone(replacement);
            replacementSnapshot.Id = existing.Id;
            if (DocumentSnapshot.AreEqual(existing, replacementSnapshot))
            {
                return Task.FromResult(new ApplicationPersistence.DocumentMutationResult(1, 0));
            }

            if (_documents.TryUpdate(existing.Id, replacementSnapshot, existing))
            {
                return Task.FromResult(new ApplicationPersistence.DocumentMutationResult(1, 1));
            }
        }
    }

    public Task<ApplicationPersistence.DocumentCompareExchangeResult> ReplaceByVersionAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DocumentVersion.ValidateExpected<TDocument>(expectedVersion);
        var predicate = filter.Compile();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = CancellationAware.Enumerate(_documents.Values, cancellationToken)
                .Where(predicate)
                .OrderBy(document => document.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (existing is null)
            {
                return Task.FromResult(new ApplicationPersistence.DocumentCompareExchangeResult(0, 0, null));
            }

            var actualVersion = DocumentVersion.Read(existing);
            if (actualVersion != expectedVersion)
            {
                throw new ApplicationPersistence.DocumentConcurrencyException(
                    existing.Id,
                    expectedVersion,
                    actualVersion);
            }

            var snapshot = DocumentSnapshot.Clone(replacement);
            snapshot.Id = existing.Id;
            var nextVersion = checked(expectedVersion + 1);
            DocumentVersion.Write(snapshot, nextVersion);
            if (_documents.TryUpdate(existing.Id, snapshot, existing))
            {
                return Task.FromResult(new ApplicationPersistence.DocumentCompareExchangeResult(1, 1, nextVersion));
            }
        }
    }

    public Task<long> DeleteByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = filter.Compile();
        var ids = CancellationAware
            .Enumerate(_documents.Values, cancellationToken)
            .Where(predicate)
            .Select(x => x.Id)
            .ToList();
        long deletedCount = 0;

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_documents.TryRemove(id, out _))
            {
                deletedCount++;
            }
        }

        return Task.FromResult(deletedCount);
    }

    public Task<ApplicationPersistence.DocumentMutationResult> UpdateOneFieldByFilterAsync<TField>(
        Expression<Func<TDocument, bool>> filter,
        Expression<Func<TDocument, TField>> field,
        TField value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var property = DocumentFieldSelector.DirectWritableProperty(field);
        var predicate = filter.Compile();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = CancellationAware.Enumerate(_documents.Values, cancellationToken)
                .Where(predicate)
                .OrderBy(document => document.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            if (existing is null)
            {
                return Task.FromResult(new ApplicationPersistence.DocumentMutationResult(0, 0));
            }

            var updated = DocumentSnapshot.Clone(existing);
            property.SetValue(updated, value);
            if (DocumentSnapshot.AreEqual(existing, updated))
            {
                return Task.FromResult(new ApplicationPersistence.DocumentMutationResult(1, 0));
            }

            if (_documents.TryUpdate(existing.Id, updated, existing))
            {
                return Task.FromResult(new ApplicationPersistence.DocumentMutationResult(1, 1));
            }
        }
    }
}

public sealed class MongoRepository<TDocument> : IMongoRepository<TDocument>
    where TDocument : class, ApplicationPersistence.IDocument
{
    private readonly IMongoCollection<TDocument> _collection;
    private readonly MongoTransactionContext? _transactionContext;

    public MongoRepository(
        IMongoDbService mongoDbService,
        MongoTransactionContext? transactionContext = null)
    {
        _collection = mongoDbService.GetCollection<TDocument>(MongoCollectionName.For<TDocument>());
        _transactionContext = transactionContext;
    }

    public async Task<TDocument> CreateAsync(
        TDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            document.Id = Guid.NewGuid().ToString("N");
        }

        DocumentVersion.Initialize(document);

        var snapshot = DocumentSnapshot.Clone(document);
        try
        {
            if (Session is { } session)
            {
                await _collection.InsertOneAsync(session, snapshot, cancellationToken: cancellationToken);
            }
            else
            {
                await _collection.InsertOneAsync(snapshot, cancellationToken: cancellationToken);
            }
        }
        catch (MongoWriteException exception) when (
            exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ApplicationPersistence.DocumentConflictException(
                $"A {typeof(TDocument).Name} document with id '{snapshot.Id}' already exists.",
                exception);
        }

        return DocumentSnapshot.Clone(snapshot);
    }

    public async Task<TDocument?> SelectAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        return await Find(filter)
            .SortBy(document => document.Id)
            .FirstOrDefaultAsync(cancellationToken);
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
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var skip = (int)Math.Min((long)(safePage - 1) * safePageSize, int.MaxValue);
        filter ??= _ => true;

        var query = Find(filter);
        if (orderBy is not null)
        {
            query = orderDescending
                ? query.SortByDescending(orderBy).ThenBy(document => document.Id)
                : query.SortBy(orderBy).ThenBy(document => document.Id);
        }
        else
        {
            query = query.SortBy(document => document.Id);
        }

        return await query
            .Skip(skip)
            .Limit(safePageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<ApplicationPersistence.DocumentCursorPage<TDocument>> ListByCursorAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        string? afterId = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var combinedFilter = filter is null
            ? Builders<TDocument>.Filter.Empty
            : Builders<TDocument>.Filter.Where(filter);

        if (afterId is not null)
        {
            combinedFilter &= Builders<TDocument>.Filter.Gt(document => document.Id, afterId);
        }

        var candidates = await Find(combinedFilter)
            .SortBy(document => document.Id)
            .Limit(safePageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = candidates.Count > safePageSize;
        var items = candidates.Take(safePageSize).ToList();

        return new ApplicationPersistence.DocumentCursorPage<TDocument>(
            items,
            hasMore ? items[^1].Id : null);
    }

    public async Task<long> CountByFilterAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= _ => true;
        return Session is { } session
            ? await _collection.CountDocumentsAsync(session, filter, cancellationToken: cancellationToken)
            : await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    public async Task<bool> ExistsByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default) =>
        (Session is { } session
            ? await _collection.CountDocumentsAsync(
                session,
                filter,
                new CountOptions { Limit = 1 },
                cancellationToken)
            : await _collection.CountDocumentsAsync(
                filter,
                new CountOptions { Limit = 1 },
                cancellationToken)) > 0;

    public async Task<ApplicationPersistence.DocumentMutationResult> ReplaceByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        CancellationToken cancellationToken = default)
    {
        var matched = await Find(filter)
            .SortBy(document => document.Id)
            .Project(document => document.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (matched is null)
        {
            return new ApplicationPersistence.DocumentMutationResult(0, 0);
        }

        var snapshot = DocumentSnapshot.Clone(replacement);
        snapshot.Id = matched;
        var identityFilter = Builders<TDocument>.Filter.Where(filter)
            & Builders<TDocument>.Filter.Eq(document => document.Id, matched);

        try
        {
            var result = Session is { } session
                ? await _collection.ReplaceOneAsync(
                    session,
                    identityFilter,
                    snapshot,
                    cancellationToken: cancellationToken)
                : await _collection.ReplaceOneAsync(
                    identityFilter,
                    snapshot,
                    cancellationToken: cancellationToken);
            return new ApplicationPersistence.DocumentMutationResult(result.MatchedCount, result.ModifiedCount);
        }
        catch (MongoWriteException exception) when (
            exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ApplicationPersistence.DocumentConflictException(
                $"Replacing {typeof(TDocument).Name} '{matched}' conflicts with an existing document.",
                exception);
        }
    }

    public async Task<ApplicationPersistence.DocumentCompareExchangeResult> ReplaceByVersionAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        DocumentVersion.ValidateExpected<TDocument>(expectedVersion);
        var current = await Find(filter)
            .SortBy(document => document.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return new ApplicationPersistence.DocumentCompareExchangeResult(0, 0, null);
        }

        var actualVersion = DocumentVersion.Read(current);
        if (actualVersion != expectedVersion)
        {
            throw new ApplicationPersistence.DocumentConcurrencyException(
                current.Id,
                expectedVersion,
                actualVersion);
        }

        var nextVersion = checked(expectedVersion + 1);
        var snapshot = DocumentSnapshot.Clone(replacement);
        snapshot.Id = current.Id;
        DocumentVersion.Write(snapshot, nextVersion);
        var versionField = DocumentVersion.Field<TDocument>();
        var identityAndVersionFilter = Builders<TDocument>.Filter.Where(filter)
            & Builders<TDocument>.Filter.Eq(document => document.Id, current.Id)
            & Builders<TDocument>.Filter.Eq(versionField, expectedVersion);
        ReplaceOneResult result;
        try
        {
            result = Session is { } session
                ? await _collection.ReplaceOneAsync(
                    session,
                    identityAndVersionFilter,
                    snapshot,
                    cancellationToken: cancellationToken)
                : await _collection.ReplaceOneAsync(
                    identityAndVersionFilter,
                    snapshot,
                    cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception) when (
            exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ApplicationPersistence.DocumentConflictException(
                $"Replacing {typeof(TDocument).Name} '{current.Id}' conflicts with an existing document.",
                exception);
        }

        if (result.MatchedCount > 0)
        {
            return new ApplicationPersistence.DocumentCompareExchangeResult(
                result.MatchedCount,
                result.ModifiedCount,
                nextVersion);
        }

        var latest = await Find(document => document.Id == current.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            return new ApplicationPersistence.DocumentCompareExchangeResult(0, 0, null);
        }

        throw new ApplicationPersistence.DocumentConcurrencyException(
            latest.Id,
            expectedVersion,
            DocumentVersion.Read(latest));
    }

    public async Task<long> DeleteByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        var result = Session is { } session
            ? await _collection.DeleteManyAsync(session, filter, cancellationToken: cancellationToken)
            : await _collection.DeleteManyAsync(filter, cancellationToken);
        return result.DeletedCount;
    }

    public async Task<ApplicationPersistence.DocumentMutationResult> UpdateOneFieldByFilterAsync<TField>(
        Expression<Func<TDocument, bool>> filter,
        Expression<Func<TDocument, TField>> field,
        TField value,
        CancellationToken cancellationToken = default)
    {
        DocumentFieldSelector.DirectWritableProperty(field);
        var matched = await Find(filter)
            .SortBy(document => document.Id)
            .Project(document => document.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (matched is null)
        {
            return new ApplicationPersistence.DocumentMutationResult(0, 0);
        }

        var update = Builders<TDocument>.Update.Set(field, value);
        var identityFilter = Builders<TDocument>.Filter.Where(filter)
            & Builders<TDocument>.Filter.Eq(document => document.Id, matched);
        var result = Session is { } session
            ? await _collection.UpdateOneAsync(
                session,
                identityFilter,
                update,
                cancellationToken: cancellationToken)
            : await _collection.UpdateOneAsync(
                identityFilter,
                update,
                cancellationToken: cancellationToken);
        return new ApplicationPersistence.DocumentMutationResult(result.MatchedCount, result.ModifiedCount);
    }

    private IClientSessionHandle? Session
    {
        get
        {
            var session = _transactionContext?.Session;
            if (session is not null)
            {
                _transactionContext!.EnsureCompatible(_collection.Database.Client);
            }

            return session;
        }
    }

    private IFindFluent<TDocument, TDocument> Find(Expression<Func<TDocument, bool>> filter) =>
        Session is { } session ? _collection.Find(session, filter) : _collection.Find(filter);

    private IFindFluent<TDocument, TDocument> Find(FilterDefinition<TDocument> filter) =>
        Session is { } session ? _collection.Find(session, filter) : _collection.Find(filter);
}

internal static class DocumentFieldSelector
{
    public static PropertyInfo DirectWritableProperty<TDocument, TField>(
        Expression<Func<TDocument, TField>> field)
    {
        Expression body = field.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } conversion)
        {
            body = conversion.Operand;
        }

        if (body is not MemberExpression
            {
                Expression: ParameterExpression,
                Member: PropertyInfo { CanWrite: true } property
            })
        {
            throw new ApplicationPersistence.DocumentQueryException(
                "Only direct writable document properties can be updated.");
        }

        return property;
    }
}

internal static class DocumentSnapshot
{
    public static TDocument Clone<TDocument>(TDocument document) =>
        BsonSerializer.Deserialize<TDocument>(document.ToBson());

    public static bool AreEqual<TDocument>(TDocument left, TDocument right) =>
        left.ToBson().AsSpan().SequenceEqual(right.ToBson());
}

internal static class DocumentVersion
{
    public static void Initialize<TDocument>(TDocument document)
    {
        if (document is ApplicationPersistence.IVersionedDocument versioned && versioned.Version <= 0)
        {
            versioned.Version = 1;
        }
    }

    public static long Read<TDocument>(TDocument document) =>
        document is ApplicationPersistence.IVersionedDocument versioned
            ? versioned.Version
            : throw NotVersioned<TDocument>();

    public static void Write<TDocument>(TDocument document, long version)
    {
        if (document is not ApplicationPersistence.IVersionedDocument versioned)
        {
            throw NotVersioned<TDocument>();
        }

        versioned.Version = version;
    }

    public static void ValidateExpected<TDocument>(long expectedVersion)
    {
        if (!typeof(ApplicationPersistence.IVersionedDocument).IsAssignableFrom(typeof(TDocument)))
        {
            throw NotVersioned<TDocument>();
        }

        if (expectedVersion <= 0 || expectedVersion == long.MaxValue)
        {
            throw new ApplicationPersistence.DocumentQueryException(
                "Expected document version must be between 1 and Int64.MaxValue - 1.");
        }
    }

    public static FieldDefinition<TDocument, long> Field<TDocument>() =>
        new StringFieldDefinition<TDocument, long>(nameof(ApplicationPersistence.IVersionedDocument.Version));

    private static ApplicationPersistence.DocumentQueryException NotVersioned<TDocument>() =>
        new($"{typeof(TDocument).Name} must implement IVersionedDocument for compare-and-swap operations.");
}

internal static class CancellationAware
{
    public static IEnumerable<T> Enumerate<T>(IEnumerable<T> source, CancellationToken cancellationToken)
    {
        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }
}

internal static class MongoCollectionName
{
    public static string For<TDocument>() =>
        typeof(TDocument).Name.Replace("Document", string.Empty).ToLowerInvariant() + "s";
}
