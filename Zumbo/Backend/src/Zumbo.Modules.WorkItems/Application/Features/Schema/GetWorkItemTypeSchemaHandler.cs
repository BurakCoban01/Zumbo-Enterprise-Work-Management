using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

public sealed class GetWorkItemTypeSchemaHandler(WorkItemTypeSchemaService service)
{
    private GetWorkItemTypeSchemaSlice? slice;

    public GetWorkItemTypeSchemaHandler(
        IDocumentRepository<WorkItemTypeSchemaDocument> schemas,
        IDocumentRepository<WorkItemDocument> workItems,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IOptions<WorkItemTypeSchemaOptions> configuredOptions,
        IClock clock)
        : this(null!)
    {
        slice = new GetWorkItemTypeSchemaSlice(new WorkItemTypeSchemaReadAccess(
            schemas, workItems, permissionChecker, currentUser, configuredOptions, clock));
    }

    public Task<WorkItemTypeSchemaResponse> HandleAsync(
        GetWorkItemTypeSchemaQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct) ?? service.GetAsync(query.ProjectId, ct);
}
