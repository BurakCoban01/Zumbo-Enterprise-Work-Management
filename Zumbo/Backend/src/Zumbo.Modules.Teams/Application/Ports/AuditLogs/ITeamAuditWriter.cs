using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Teams;

public interface ITeamAuditWriter
{
    Task WriteAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}
