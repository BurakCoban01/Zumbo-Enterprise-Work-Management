namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemBulkJobOptions
{
    public int BatchSize { get; init; } = 25;
    public int MaxInputItems { get; init; } = 5_000;
    public int MaxInputBytes { get; init; } = 5 * 1024 * 1024;
    public int MaxExportItems { get; init; } = 10_000;
    public long MaxArtifactBytes { get; init; } = 25 * 1024 * 1024;
    public int ArtifactRetentionDays { get; init; } = 7;
}
