using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed class CreateTeamValidator
{
    public static void Validate(CreateTeamRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationId)
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            throw new ValidationException("Organization id, team name and owner user id are required.");
        }
    }
}
