using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

public sealed class GetIssueTypeHierarchyHandler(WorkItemTypeSchemaService service)
{
    private GetIssueTypeHierarchySlice? slice;

    public GetIssueTypeHierarchyHandler(
        IDocumentRepository<WorkItemTypeSchemaDocument> schemas,
        IClock clock)
        : this(null!) =>
        slice = new GetIssueTypeHierarchySlice(new WorkItemTypeSchemaPolicyAccess(schemas, clock));

    public Task<string> HandleAsync(GetIssueTypeHierarchyQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.HierarchyLevelAsync(query.ProjectId, query.IssueTypeKey, ct);
}
