using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;
public sealed record ApiKeyPrincipal(
    string ApiKeyId,
    string UserId,
    string Username,
    string Email,
    string OrganizationId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Scopes);
