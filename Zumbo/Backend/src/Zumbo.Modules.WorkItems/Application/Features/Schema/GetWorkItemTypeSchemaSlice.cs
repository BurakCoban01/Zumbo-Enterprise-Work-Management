namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

internal sealed class GetWorkItemTypeSchemaSlice(WorkItemTypeSchemaReadAccess access)
{
    internal async Task<WorkItemTypeSchemaResponse> HandleAsync(
        GetWorkItemTypeSchemaQuery query,
        CancellationToken ct)
    {
        await access.EnsureViewAsync(query.ProjectId, ct);
        return WorkItemTypeSchemaResponseMapper.ToResponse(
            await access.LoadOrDefaultAsync(query.ProjectId, ct));
    }
}
