namespace Zumbo.BuildingBlocks.Application.Runtime;

public sealed class ExternalDependencyTimeoutException(string dependency, string operation, Exception? innerException = null)
    : TimeoutException($"External dependency '{dependency}' timed out during '{operation}'.", innerException);
