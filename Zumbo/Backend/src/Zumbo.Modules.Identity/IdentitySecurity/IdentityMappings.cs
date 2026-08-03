using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Identity;

public static class IdentityMappings
{
    public static UserProfileResponse ToProfile(this UserDocument user) =>
        new(user.Id, user.Username, user.Email, user.OrganizationId, user.Roles, user.Version);
}
