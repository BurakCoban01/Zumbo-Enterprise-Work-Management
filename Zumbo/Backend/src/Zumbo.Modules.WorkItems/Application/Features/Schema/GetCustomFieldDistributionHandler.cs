using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

public sealed class GetCustomFieldDistributionHandler(WorkItemTypeSchemaService service)
{
    private GetCustomFieldDistributionSlice? slice;

    public GetCustomFieldDistributionHandler(
        IDocumentRepository<WorkItemTypeSchemaDocument> schemas,
        IDocumentRepository<WorkItemDocument> workItems,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IOptions<WorkItemTypeSchemaOptions> configuredOptions,
        IClock clock)
        : this(null!)
    {
        slice = new GetCustomFieldDistributionSlice(new WorkItemTypeSchemaReadAccess(
            schemas, workItems, permissionChecker, currentUser, configuredOptions, clock));
    }

    public Task<WorkItemFieldDistributionResponse> HandleAsync(
        GetCustomFieldDistributionQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.GetCustomFieldDistributionAsync(query.ProjectId, query.FieldKey, ct);
}
