using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CustomFieldDefinitionRequest(
    string Key,
    string Name,
    string Type,
    bool Required,
    bool Indexed,
    int? MaxLength,
    decimal? Minimum,
    decimal? Maximum,
    IReadOnlyCollection<string>? Options,
    IReadOnlyCollection<string>? AppliesToIssueTypes,
    int Position = 0);
