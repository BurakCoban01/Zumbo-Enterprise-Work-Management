using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

public sealed class GetIssueTypeDistributionHandler(WorkItemTypeSchemaService service)
{
    private GetIssueTypeDistributionSlice? slice;

    public GetIssueTypeDistributionHandler(
        IDocumentRepository<WorkItemTypeSchemaDocument> schemas,
        IDocumentRepository<WorkItemDocument> workItems,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IOptions<WorkItemTypeSchemaOptions> configuredOptions,
        IClock clock)
        : this(null!)
    {
        slice = new GetIssueTypeDistributionSlice(new WorkItemTypeSchemaReadAccess(
            schemas, workItems, permissionChecker, currentUser, configuredOptions, clock));
    }

    public Task<WorkItemFieldDistributionResponse> HandleAsync(
        GetIssueTypeDistributionQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.GetIssueTypeDistributionAsync(query.ProjectId, ct);
}
