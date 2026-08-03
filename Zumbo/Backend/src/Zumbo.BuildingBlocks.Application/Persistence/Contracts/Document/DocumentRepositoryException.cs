using System.Linq.Expressions;

namespace Zumbo.BuildingBlocks.Application.Persistence;

public abstract class DocumentRepositoryException : Exception
{
    protected DocumentRepositoryException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
