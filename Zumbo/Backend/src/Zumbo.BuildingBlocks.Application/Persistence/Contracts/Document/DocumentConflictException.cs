using System.Linq.Expressions;

namespace Zumbo.BuildingBlocks.Application.Persistence;

public sealed class DocumentConflictException : DocumentRepositoryException
{
    public DocumentConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
