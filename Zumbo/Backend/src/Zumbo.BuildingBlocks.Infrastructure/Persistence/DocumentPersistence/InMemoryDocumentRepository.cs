using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

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
