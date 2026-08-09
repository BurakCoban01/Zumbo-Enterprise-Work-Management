using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

public sealed class ValidateWorkItemSearchFilterHandler(WorkItemTypeSchemaService service)
{
    private ValidateWorkItemSearchFilterSlice? slice;

    public ValidateWorkItemSearchFilterHandler(
        IDocumentRepository<WorkItemTypeSchemaDocument> schemas,
        IClock clock)
        : this(null!) =>
        slice = new ValidateWorkItemSearchFilterSlice(new WorkItemTypeSchemaPolicyAccess(schemas, clock));

    public Task<ValidatedWorkItemSearchFilter> HandleAsync(
        ValidateWorkItemSearchFilterQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ValidateSearchFilterAsync(
            query.ProjectId,
            query.IssueTypeKey,
            query.CustomFieldKey,
            query.CustomFieldValue,
            ct);
}
