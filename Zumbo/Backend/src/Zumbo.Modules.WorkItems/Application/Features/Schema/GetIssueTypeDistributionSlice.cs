namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

internal sealed class GetIssueTypeDistributionSlice(WorkItemTypeSchemaReadAccess access)
{
    internal async Task<WorkItemFieldDistributionResponse> HandleAsync(
        GetIssueTypeDistributionQuery query,
        CancellationToken ct)
    {
        await access.EnsureViewAsync(query.ProjectId, ct);
        return await access.BuildDistributionAsync(query.ProjectId, "Type", item => item.Type, ct);
    }
}
