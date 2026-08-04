using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemTypeSchemaResponse(
    string ProjectId,
    int SchemaVersion,
    IReadOnlyCollection<IssueTypeDefinitionRequest> IssueTypes,
    IReadOnlyCollection<CustomFieldDefinitionRequest> CustomFields,
    IReadOnlyCollection<IssueTypeLayoutRequest> Layouts,
    long Version) : IVersionedResource;
