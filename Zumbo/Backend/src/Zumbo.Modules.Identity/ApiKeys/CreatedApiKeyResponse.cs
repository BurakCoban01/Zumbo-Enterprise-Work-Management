using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;
public sealed record CreatedApiKeyResponse(
    string Id,
    string Name,
    string Key,
    string KeyPrefix,
    IReadOnlyCollection<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
