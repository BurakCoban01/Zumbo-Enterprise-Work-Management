using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public static class AttachmentSecurityStates
{
    public const string Quarantined = "Quarantined";
    public const string Clean = "Clean";
    public const string Rejected = "Rejected";
}

public static class AttachmentMalwareScanStatuses
{
    public const string Clean = "Clean";
    public const string Infected = "Infected";
    public const string Unavailable = "Unavailable";
}

public sealed record AttachmentMalwareScanResult(string Status, string Provider, string? Detail = null);

public interface IAttachmentMalwareScanner
{
    Task<AttachmentMalwareScanResult> ScanAsync(
        Stream content,
        string fileName,
        CancellationToken ct);
}

public sealed record StoredAttachment(
    string FileName,
    string ContentType,
    long SizeBytes,
    string StoragePath,
    string ChecksumSha256,
    string SecurityState = AttachmentSecurityStates.Clean,
    string ScanProvider = "Policy",
    string? ScanDetail = null,
    DateTimeOffset? ScannedAt = null);

public sealed record StoredAttachmentObject(string StoragePath, DateTimeOffset CreatedAt);

public interface IAttachmentStorage
{
    Task<StoredAttachment> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken ct);

    Task<StoredAttachment> ReprocessAsync(StoredAttachment attachment, CancellationToken ct);
    Task<Stream> OpenReadAsync(
        string storagePath,
        string contentType,
        string expectedChecksumSha256,
        CancellationToken ct);
    Task<IReadOnlyList<StoredAttachmentObject>> ListObjectsAsync(int maxCount, CancellationToken ct);
    Task DeleteAsync(string storagePath, CancellationToken ct);
}

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

public sealed record AttachmentMaintenanceResult(
    int Retried,
    int Cleaned,
    int Rejected,
    int PurgedMetadata,
    int DeletedOrphans);

public sealed class AttachmentSecurityMaintenanceService(
    IDocumentRepository<WorkItemAttachmentActivityDocument> attachments,
    IAttachmentStorage storage,
    IOptions<AttachmentSecurityOptions> options,
    IClock clock)
{
    public async Task<AttachmentMaintenanceResult> RunBatchAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var batchSize = Math.Clamp(options.Value.MaintenanceBatchSize, 1, 500);
        var quarantineCutoff = now.AddHours(-Math.Clamp(options.Value.QuarantineRetentionHours, 1, 24 * 30));
        var rejectedCutoff = now.AddDays(-Math.Clamp(options.Value.RejectedMetadataRetentionDays, 1, 365));
        var orphanCutoff = now.AddHours(-Math.Clamp(options.Value.OrphanRetentionHours, 1, 24 * 30));
        var retried = 0;
        var cleaned = 0;
        var rejected = 0;
        var purgedMetadata = 0;
        var deletedOrphans = 0;

        var quarantined = await attachments.ListByFilterAsync(
            x => x.SecurityState == AttachmentSecurityStates.Quarantined,
            x => x.CreatedAt,
            pageSize: batchSize,
            cancellationToken: ct);
        foreach (var attachment in quarantined)
        {
            if (attachment.CreatedAt <= quarantineCutoff)
            {
                var deleted = await DeleteByVersionAsync(attachment, ct);
                if (deleted > 0)
                {
                    await storage.DeleteAsync(attachment.StoragePath, ct);
                    purgedMetadata += checked((int)deleted);
                }
                continue;
            }

            retried++;
            var outcome = await storage.ReprocessAsync(ToStored(attachment), ct);
            attachment.StoragePath = outcome.StoragePath;
            attachment.SecurityState = outcome.SecurityState;
            attachment.ScanProvider = outcome.ScanProvider;
            attachment.ScanDetail = outcome.ScanDetail;
            attachment.ScannedAt = outcome.ScannedAt;
            var replaced = await attachments.ReplaceByVersionAsync(
                x => x.Id == attachment.Id
                    && x.OrganizationId == attachment.OrganizationId
                    && x.ProjectId == attachment.ProjectId
                    && x.WorkItemId == attachment.WorkItemId,
                attachment,
                attachment.Version,
                ct);
            if (!replaced.Found)
            {
                continue;
            }

            if (outcome.SecurityState == AttachmentSecurityStates.Quarantined)
            {
                continue;
            }

            if (outcome.SecurityState == AttachmentSecurityStates.Clean)
            {
                cleaned++;
            }
            else
            {
                rejected++;
            }
        }

        var rejectedDocuments = await attachments.ListByFilterAsync(
            x => x.SecurityState == AttachmentSecurityStates.Rejected && x.CreatedAt <= rejectedCutoff,
            x => x.CreatedAt,
            pageSize: batchSize,
            cancellationToken: ct);
        foreach (var attachment in rejectedDocuments)
        {
            var deleted = await DeleteByVersionAsync(attachment, ct);
            if (deleted > 0 && !string.IsNullOrWhiteSpace(attachment.StoragePath))
            {
                await storage.DeleteAsync(attachment.StoragePath, ct);
            }
            purgedMetadata += checked((int)deleted);
        }

        var objects = await storage.ListObjectsAsync(batchSize, ct);
        foreach (var storedObject in objects.Where(x => x.CreatedAt <= orphanCutoff))
        {
            if (!await attachments.ExistsByFilterAsync(x => x.StoragePath == storedObject.StoragePath, ct))
            {
                await storage.DeleteAsync(storedObject.StoragePath, ct);
                deletedOrphans++;
            }
        }

        return new AttachmentMaintenanceResult(retried, cleaned, rejected, purgedMetadata, deletedOrphans);
    }

    private Task<long> DeleteByVersionAsync(
        WorkItemAttachmentActivityDocument attachment,
        CancellationToken ct) =>
        attachments.DeleteByFilterAsync(
            x => x.Id == attachment.Id
                && x.OrganizationId == attachment.OrganizationId
                && x.ProjectId == attachment.ProjectId
                && x.WorkItemId == attachment.WorkItemId
                && x.Version == attachment.Version,
            ct);

    private static StoredAttachment ToStored(WorkItemAttachmentActivityDocument attachment) =>
        new(
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.StoragePath,
            attachment.ChecksumSha256,
            attachment.SecurityState,
            attachment.ScanProvider,
            attachment.ScanDetail,
            attachment.ScannedAt);
}
