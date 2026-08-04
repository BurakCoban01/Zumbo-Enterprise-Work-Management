using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed record CreateApiKeyRequest(
    string Name,
    string Password,
    string? MfaCode,
    DateTimeOffset? ExpiresAt,
    IReadOnlyCollection<string>? Scopes);
