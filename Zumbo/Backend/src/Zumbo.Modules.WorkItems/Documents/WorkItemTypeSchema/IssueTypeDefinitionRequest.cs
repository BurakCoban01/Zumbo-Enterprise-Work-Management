using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record IssueTypeDefinitionRequest(
    string Key,
    string Name,
    string? Description,
    string HierarchyLevel,
    bool Active = true,
    int Position = 0);
