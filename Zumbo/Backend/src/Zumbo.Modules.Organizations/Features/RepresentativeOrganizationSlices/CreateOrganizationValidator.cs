using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed class CreateOrganizationValidator
{
    public static void Validate(CreateOrganizationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.TenantKey))
        {
            throw new ValidationException("Organization name and tenant key are required.");
        }
    }
}
