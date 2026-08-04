using Zumbo.Modules.Audit;

public sealed class HttpAuditRequestContext(IHttpContextAccessor httpContextAccessor) : IAuditRequestContext
{
    public AuditRequestMetadata GetMetadata()
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null)
        {
            return new AuditRequestMetadata(null, null);
        }

        return new AuditRequestMetadata(
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString());
    }
}
