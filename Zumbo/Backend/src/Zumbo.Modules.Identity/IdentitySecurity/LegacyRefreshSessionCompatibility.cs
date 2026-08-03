using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Identity;

public static class LegacyRefreshSessionCompatibility
{
    public static bool RevokeAll(UserDocument user, DateTimeOffset revokedAt)
    {
        var changed = false;
        foreach (var token in user.RefreshTokens.Where(token => token.RevokedAt is null))
        {
            token.RevokedAt = revokedAt;
            changed = true;
        }

        return changed;
    }
}
