namespace Zumbo.BuildingBlocks.Application.Runtime;

public enum ExternalDependencyOperationKind
{
    Read,
    IdempotentWrite,
    NonIdempotentWrite,
    Health
}
