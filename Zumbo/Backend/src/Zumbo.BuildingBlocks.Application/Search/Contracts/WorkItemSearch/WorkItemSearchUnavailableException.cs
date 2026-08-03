namespace Zumbo.BuildingBlocks.Application.Search;

public sealed class WorkItemSearchUnavailableException : Exception
{
    public WorkItemSearchUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
