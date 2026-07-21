using System.Security.Cryptography;
using System.Text;
using Zumbo.Modules.Identity;

public sealed class SessionClientContextAdapter(IHttpContextAccessor accessor) : ISessionClientContext
{
    public SessionClientInfo GetClientInfo()
    {
        var request = accessor.HttpContext?.Request;
        if (request is null)
        {
            return new SessionClientInfo("Unknown client", string.Empty);
        }

        var userAgent = request.Headers.UserAgent.ToString();
        var requestedName = request.Headers["X-Zumbo-Device-Name"].ToString();
        var deviceName = string.IsNullOrWhiteSpace(requestedName)
            ? string.IsNullOrWhiteSpace(userAgent) ? "API client" : "Browser"
            : requestedName;
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(userAgent)));
        return new SessionClientInfo(deviceName, fingerprint);
    }
}
