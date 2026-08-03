using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    public async Task<WorkItemFieldDistributionResponse> GetCustomFieldDistributionAsync(
        string projectId,
        string fieldKey,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var schema = await LoadOrDefaultAsync(projectId, ct);
        var key = NormalizeKey(fieldKey);
        var field = schema.CustomFields.SingleOrDefault(item =>
                item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException($"Custom field '{key}' is not defined.");
        return await BuildDistributionAsync(
            projectId,
            field.Key,
            item => item.CustomFields.SingleOrDefault(value => value.FieldKey == field.Key)?.SearchValue,
            ct);
    }
}
