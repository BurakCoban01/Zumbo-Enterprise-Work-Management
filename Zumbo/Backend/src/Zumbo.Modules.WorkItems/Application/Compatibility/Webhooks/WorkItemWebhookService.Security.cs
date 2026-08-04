using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService
{
    public static string Sign(string secret, long timestampUnixSeconds, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes($"{timestampUnixSeconds}.{payload}"))).ToLowerInvariant();
    }
}
