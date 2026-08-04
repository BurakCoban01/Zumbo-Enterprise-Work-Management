using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemGraphOptions
{
    public int MaxTraversalDepth { get; init; } = 64;
    public int MaxVisitedNodes { get; init; } = 1_000;
    public int MaxOutgoingDependenciesPerNode { get; init; } = 200;
    public int MaxRelationsPerWorkItem { get; init; } = 200;
    public int MaxChildrenPerWorkItem { get; init; } = 200;
}
