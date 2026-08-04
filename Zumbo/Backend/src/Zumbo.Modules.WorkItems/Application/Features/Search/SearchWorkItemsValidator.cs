using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class SearchWorkItemsValidator
{
    public static void Validate(WorkItemSearchRequest request)
    {
        ValidateProjectScope(request);
        ValidateText(request);
        if (request.CustomFieldKey?.Trim().Length > 40 || request.CustomFieldValue?.Trim().Length > 4_000)
        {
            throw new ValidationException("Custom field search filter is too long.");
        }
        if (string.IsNullOrWhiteSpace(request.CustomFieldKey) != string.IsNullOrWhiteSpace(request.CustomFieldValue))
        {
            throw new ValidationException("Custom field key and value must be supplied together.");
        }
    }

    public static void ValidateProjectScope(WorkItemSearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            throw new ValidationException("Project id is required for work item search.");
        }
    }

    public static void ValidateText(WorkItemSearchRequest request)
    {
        if (request.Text?.Trim().Length > 200)
        {
            throw new ValidationException("Search text cannot exceed 200 characters.");
        }
    }
}
