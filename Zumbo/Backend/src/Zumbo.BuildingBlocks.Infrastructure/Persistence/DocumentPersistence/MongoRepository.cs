using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

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
