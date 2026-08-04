using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record UpsertWorkItemTypeSchemaRequest(
    IReadOnlyCollection<IssueTypeDefinitionRequest> IssueTypes,
    IReadOnlyCollection<CustomFieldDefinitionRequest>? CustomFields,
    IReadOnlyCollection<IssueTypeLayoutRequest>? Layouts);
