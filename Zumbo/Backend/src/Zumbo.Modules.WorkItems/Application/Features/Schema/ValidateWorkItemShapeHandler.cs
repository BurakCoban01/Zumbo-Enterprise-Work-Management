using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

public sealed class ValidateWorkItemShapeHandler(WorkItemTypeSchemaService service)
{
    private ValidateWorkItemShapeSlice? slice;

    public ValidateWorkItemShapeHandler(
        IDocumentRepository<WorkItemTypeSchemaDocument> schemas,
        IClock clock)
        : this(null!) =>
        slice = new ValidateWorkItemShapeSlice(new WorkItemTypeSchemaPolicyAccess(schemas, clock));

    public Task<ValidatedWorkItemShape> HandleAsync(
        ValidateWorkItemShapeQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ValidateAsync(query.ProjectId, query.IssueTypeKey, query.Values, ct);
}
