using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record ValidatedWorkItemSearchFilter(
    string? IssueType,
    string? CustomFieldKey,
    string? CustomFieldValue);
