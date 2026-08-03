using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

public sealed record BrowserSessionResponse(
    UserProfileResponse User,
    DateTimeOffset ExpiresAt,
    string CsrfToken);
