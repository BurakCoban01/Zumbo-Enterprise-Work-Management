namespace Zumbo.BuildingBlocks.Application.Runtime;

public sealed class ExternalDependencyTransientException(string safeReason, Exception? innerException = null)
    : Exception(safeReason, innerException);
