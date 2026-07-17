using System.Linq.Expressions;

namespace Zumbo.BuildingBlocks.Application.Persistence;

public interface IDocument
{
    string Id { get; set; }
}

public interface IVersionedDocument : IDocument
{
    long Version { get; set; }
}

public interface IVersionedResource
{
    long Version { get; }
}

public interface IExpectedVersionAccessor
{
    long? ExpectedVersion { get; }
}

public sealed class ExpectedVersionState(IExpectedVersionAccessor? accessor)
{
    private bool consumed;

    public long Consume(long currentVersion)
    {
        if (consumed || accessor?.ExpectedVersion is not long expectedVersion)
        {
            return currentVersion;
        }

        consumed = true;
        return expectedVersion;
    }
}

public readonly record struct DocumentMutationResult(long MatchedCount, long ModifiedCount)
{
    public bool Found => MatchedCount > 0;
    public bool Changed => ModifiedCount > 0;
}

public readonly record struct DocumentCompareExchangeResult(
    long MatchedCount,
    long ModifiedCount,
    long? Version)
{
    public bool Found => MatchedCount > 0;
    public bool Changed => ModifiedCount > 0;
}

public sealed record DocumentCursorPage<TDocument>(
    IReadOnlyList<TDocument> Items,
    string? NextCursor);

public abstract class DocumentRepositoryException : Exception
{
    protected DocumentRepositoryException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class DocumentConflictException : DocumentRepositoryException
{
    public DocumentConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class DocumentQueryException : DocumentRepositoryException
{
    public DocumentQueryException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class DocumentConcurrencyException : DocumentRepositoryException
{
    public DocumentConcurrencyException(
        string documentId,
        long expectedVersion,
        long actualVersion)
        : base($"Document '{documentId}' has version {actualVersion}; expected version was {expectedVersion}.")
    {
        DocumentId = documentId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public string DocumentId { get; }
    public long ExpectedVersion { get; }
    public long ActualVersion { get; }
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

    Task<DocumentCursorPage<TDocument>> ListByCursorAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        string? afterId = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default);

    Task<long> CountByFilterAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default);

    Task<DocumentMutationResult> ReplaceByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        CancellationToken cancellationToken = default);

    Task<DocumentCompareExchangeResult> ReplaceByVersionAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        long expectedVersion,
        CancellationToken cancellationToken = default);

    Task<long> DeleteByFilterAsync(
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default);

    Task<DocumentMutationResult> UpdateOneFieldByFilterAsync<TField>(
        Expression<Func<TDocument, bool>> filter,
        Expression<Func<TDocument, TField>> field,
        TField value,
        CancellationToken cancellationToken = default);
}
