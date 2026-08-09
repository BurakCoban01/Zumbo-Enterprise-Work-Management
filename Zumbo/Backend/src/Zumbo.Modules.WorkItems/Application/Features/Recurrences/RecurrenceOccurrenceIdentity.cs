using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal static class RecurrenceOccurrenceIdentity
{
    internal static string Create(string recurrenceId, DateTimeOffset scheduledForUtc)
    {
        var input = $"{recurrenceId}\u001f{scheduledForUtc.ToUniversalTime().UtcTicks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))
            .ToLowerInvariant()[..32];
    }
}
