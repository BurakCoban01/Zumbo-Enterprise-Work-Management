using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class AttachmentSecurityOptions
{
    public string ScannerProvider { get; init; } = "PolicyOnly";
    public int MaxArchiveEntries { get; init; } = 1_000;
    public long MaxArchiveExpandedBytes { get; init; } = 100 * 1024 * 1024;
    public int MaxArchiveCompressionRatio { get; init; } = 100;
    public int QuarantineRetentionHours { get; init; } = 24;
    public int RejectedMetadataRetentionDays { get; init; } = 30;
    public int OrphanRetentionHours { get; init; } = 24;
    public int MaintenanceBatchSize { get; init; } = 100;
    public int MaintenanceIntervalMinutes { get; init; } = 15;
    public string ClamAvHost { get; init; } = string.Empty;
    public int ClamAvPort { get; init; } = 3310;
    public int ClamAvTimeoutSeconds { get; init; } = 10;
}
