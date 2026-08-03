namespace Zumbo.BuildingBlocks.Application.Runtime;

public interface IExternalDependencyPolicy
{
    Task<T> ExecuteAsync<T>(
        string operation,
        ExternalDependencyOperationKind operationKind,
        Func<CancellationToken, Task<T>> action,
        Func<Exception, bool>? isTransient = null,
        CancellationToken cancellationToken = default);

    Task ExecuteAsync(
        string operation,
        ExternalDependencyOperationKind operationKind,
        Func<CancellationToken, Task> action,
        Func<Exception, bool>? isTransient = null,
        CancellationToken cancellationToken = default);
}
