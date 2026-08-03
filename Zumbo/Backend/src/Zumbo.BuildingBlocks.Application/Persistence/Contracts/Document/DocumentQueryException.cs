using System.Linq.Expressions;

namespace Zumbo.BuildingBlocks.Application.Persistence;

public sealed class DocumentQueryException : DocumentRepositoryException
{
    public DocumentQueryException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
