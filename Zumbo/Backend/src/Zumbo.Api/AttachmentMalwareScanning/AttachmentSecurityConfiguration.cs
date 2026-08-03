using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.Modules.WorkItems;

internal static class AttachmentSecurityConfiguration
{
    internal static bool IsValid(AttachmentSecurityOptions options)
    {
        if (options.ScannerProvider is not ("PolicyOnly" or "ClamAv")
            || options.MaxArchiveEntries is < 1 or > 10_000
            || options.MaxArchiveExpandedBytes is < 1_048_576 or > 524_288_000
            || options.MaxArchiveCompressionRatio is < 2 or > 1_000
            || options.QuarantineRetentionHours is < 1 or > 720
            || options.RejectedMetadataRetentionDays is < 1 or > 365
            || options.OrphanRetentionHours is < 1 or > 720
            || options.MaintenanceBatchSize is < 1 or > 500
            || options.MaintenanceIntervalMinutes is < 1 or > 1_440)
        {
            return false;
        }

        return options.ScannerProvider != "ClamAv"
            || (!string.IsNullOrWhiteSpace(options.ClamAvHost)
                && options.ClamAvPort is >= 1 and <= 65_535
                && options.ClamAvTimeoutSeconds is >= 1 and <= 120);
    }
}
