namespace Zumbo.BuildingBlocks.Application.Runtime;

public sealed class ExternalDependencyBulkheadRejectedException(string dependency)
    : InvalidOperationException($"External dependency '{dependency}' bulkhead is saturated.");
