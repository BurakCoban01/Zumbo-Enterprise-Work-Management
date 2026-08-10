using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

internal sealed class GetCustomFieldDistributionSlice(WorkItemTypeSchemaReadAccess access)
{
    internal async Task<WorkItemFieldDistributionResponse> HandleAsync(
        GetCustomFieldDistributionQuery query,
        CancellationToken ct)
    {
        await access.EnsureViewAsync(query.ProjectId, ct);
        var schema = await access.LoadOrDefaultAsync(query.ProjectId, ct);
        var key = query.FieldKey?.Trim() ?? string.Empty;
        var field = schema.CustomFields.SingleOrDefault(item =>
                item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException($"Custom field '{key}' is not defined.");
        return await access.BuildDistributionAsync(
            query.ProjectId,
            field.Key,
            item => item.CustomFields.SingleOrDefault(value => value.FieldKey == field.Key)?.SearchValue,
            ct);
    }
}
