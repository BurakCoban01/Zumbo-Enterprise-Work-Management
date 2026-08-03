namespace Zumbo.BuildingBlocks.Application.Runtime;

public sealed class ExternalDependencyCircuitOpenException(string dependency)
    : InvalidOperationException($"External dependency '{dependency}' circuit is open.");
