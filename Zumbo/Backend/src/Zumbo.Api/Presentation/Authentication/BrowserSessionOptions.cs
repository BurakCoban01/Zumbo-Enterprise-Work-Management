using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

public sealed class BrowserSessionOptions
{
    public string AccessCookieName { get; init; } = "zumbo-access";
    public string RefreshCookieName { get; init; } = "zumbo-refresh";
    public string CsrfCookieName { get; init; } = "zumbo-csrf";
    public string CsrfHeaderName { get; init; } = "X-CSRF-Token";
    public bool SecureCookies { get; init; } = true;
    public int RefreshCookieDays { get; init; } = 14;
}
