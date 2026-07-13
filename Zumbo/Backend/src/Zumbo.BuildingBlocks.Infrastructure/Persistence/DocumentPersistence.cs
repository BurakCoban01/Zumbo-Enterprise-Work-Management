using System.Collections.Concurrent;
using System.Linq.Expressions;
using MongoDB.Driver;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

public interface IDocument
{
    string Id { get; set; }
}

public interface IDocumentRepository<TDocument>
    where TDocument : class, IDocument
{
    Task<TDocument> CreateAsync(TDocument document, CancellationToken cancellationToken = default);

    Task<TDocument?> SelectAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TDocument>> ListByFilterAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        Expression<Func<TDocument, object>>? orderBy = null,
        bool orderDescending = false,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<bool> ReplaceByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        CancellationToken cancellationToken = default);

    Task<long> DeleteByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default);

    Task<long> UpdateOneFieldByFilterAsync<TField>(
        Expression<Func<TDocument, bool>> filter,
        Expression<Func<TDocument, TField>> field,
        TField value,
        CancellationToken cancellationToken = default);
}

public interface IMongoRepository<TDocument> : IDocumentRepository<TDocument>
    where TDocument : class, IDocument;

public sealed class InMemoryDocumentRepository<TDocument> : IDocumentRepository<TDocument>
    where TDocument : class, IDocument
{
    private readonly ConcurrentDictionary<string, TDocument> _documents = new();

    public Task<TDocument> CreateAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            document.Id = Guid.NewGuid().ToString("N");
        }

        _documents[document.Id] = document;
        return Task.FromResult(document);
    }

    public Task<TDocument?> SelectAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        var predicate = filter.Compile();
        return Task.FromResult(_documents.Values.FirstOrDefault(predicate));
    }

    public Task<IReadOnlyList<TDocument>> ListByFilterAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        Expression<Func<TDocument, object>>? orderBy = null,
        bool orderDescending = false,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var predicate = (filter ?? (_ => true)).Compile();

        IEnumerable<TDocument> query = _documents.Values.Where(predicate);

        if (orderBy is not null)
        {
            var keySelector = orderBy.Compile();
            query = orderDescending
                ? query.OrderByDescending(keySelector)
                : query.OrderBy(keySelector);
        }

        IReadOnlyList<TDocument> result = query
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<bool> ReplaceByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        CancellationToken cancellationToken = default)
    {
        var predicate = filter.Compile();
        var existing = _documents.Values.FirstOrDefault(predicate);

        if (existing is null)
        {
            return Task.FromResult(false);
        }

        replacement.Id = existing.Id;
        _documents[existing.Id] = replacement;
        return Task.FromResult(true);
    }

    public Task<long> DeleteByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        var predicate = filter.Compile();
        var ids = _documents.Values.Where(predicate).Select(x => x.Id).ToList();

        foreach (var id in ids)
        {
            _documents.TryRemove(id, out _);
        }

        return Task.FromResult((long)ids.Count);
    }

    public Task<long> UpdateOneFieldByFilterAsync<TField>(
        Expression<Func<TDocument, bool>> filter,
        Expression<Func<TDocument, TField>> field,
        TField value,
        CancellationToken cancellationToken = default)
    {
        var target = _documents.Values.FirstOrDefault(filter.Compile());
        if (target is null)
        {
            return Task.FromResult(0L);
        }

        if (field.Body is not MemberExpression memberExpression)
        {
            throw new InvalidOperationException("Only direct property update expressions are supported.");
        }

        var property = typeof(TDocument).GetProperty(memberExpression.Member.Name);
        property?.SetValue(target, value);
        return Task.FromResult(1L);
    }
}

public sealed class MongoRepository<TDocument> : IMongoRepository<TDocument>
    where TDocument : class, IDocument
{
    private readonly IMongoCollection<TDocument> _collection;

    public MongoRepository(IMongoDbService mongoDbService)
    {
        _collection = mongoDbService.GetCollection<TDocument>(MongoCollectionName.For<TDocument>());
    }

    public async Task<TDocument> CreateAsync(
        TDocument document,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            document.Id = Guid.NewGuid().ToString("N");
        }

        await _collection.InsertOneAsync(document, cancellationToken: cancellationToken);
        return document;
    }

    public async Task<TDocument?> SelectAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
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
        filter ??= _ => true;

        var query = _collection.Find(filter);
        if (orderBy is not null)
        {
            query = orderDescending ? query.SortByDescending(orderBy) : query.SortBy(orderBy);
        }

        return await query
            .Skip((safePage - 1) * safePageSize)
            .Limit(safePageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ReplaceByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        CancellationToken cancellationToken = default)
    {
        var result = await _collection.ReplaceOneAsync(filter, replacement, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    public async Task<long> DeleteByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        var result = await _collection.DeleteManyAsync(filter, cancellationToken);
        return result.DeletedCount;
    }

    public async Task<long> UpdateOneFieldByFilterAsync<TField>(
        Expression<Func<TDocument, bool>> filter,
        Expression<Func<TDocument, TField>> field,
        TField value,
        CancellationToken cancellationToken = default)
    {
        var update = Builders<TDocument>.Update.Set(field, value);
        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount;
    }
}

internal static class MongoCollectionName
{
    public static string For<TDocument>() =>
        typeof(TDocument).Name.Replace("Document", string.Empty).ToLowerInvariant() + "s";
}
