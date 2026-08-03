using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed class CreateProjectValidator
{
    public static void Validate(CreateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationId)
            || string.IsNullOrWhiteSpace(request.Key)
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            throw new ValidationException("Organization id, project key and name are required.");
        }
    }
}
