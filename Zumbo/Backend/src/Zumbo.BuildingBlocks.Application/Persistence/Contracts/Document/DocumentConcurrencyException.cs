using System.Linq.Expressions;

namespace Zumbo.BuildingBlocks.Application.Persistence;

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
