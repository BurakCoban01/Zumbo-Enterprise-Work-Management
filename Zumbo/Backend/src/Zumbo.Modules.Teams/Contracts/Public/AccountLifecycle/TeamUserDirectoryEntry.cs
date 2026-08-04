using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Teams;
public sealed record TeamUserDirectoryEntry(
    string Id,
    string Email,
    string OrganizationId,
    bool IsActive,
    string? DisplayName = null);
