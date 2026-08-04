using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Identity;

public static class RefreshTokenSecurity
{
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
