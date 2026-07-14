using Microsoft.AspNetCore.DataProtection;
using Zumbo.Modules.Identity;

public sealed class MfaSecretProtectorAdapter(IDataProtectionProvider provider) : IMfaSecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Zumbo.Identity.MfaSecret.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
