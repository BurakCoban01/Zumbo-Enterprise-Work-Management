using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class AttachmentSecurityMaintenanceService(
    IDocumentRepository<WorkItemAttachmentActivityDocument> attachments,
    IAttachmentStorage storage,
    IOptions<AttachmentSecurityOptions> options,
    IClock clock)
{
    public Task<AttachmentMaintenanceResult> RunBatchAsync(CancellationToken ct) =>
        RunBatchAsync(null, ct);

    public async Task<AttachmentSecurityStatus> GetStatusAsync(
        string organizationId,
        CancellationToken ct)
    {
        organizationId = RequiredOrganizationId(organizationId);
        var quarantined = await attachments.CountByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.SecurityState == AttachmentSecurityStates.Quarantined,
            ct);
        var clean = await attachments.CountByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.SecurityState == AttachmentSecurityStates.Clean,
            ct);
        var rejected = await attachments.CountByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.SecurityState == AttachmentSecurityStates.Rejected,
            ct);
        var oldest = (await attachments.ListByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.SecurityState == AttachmentSecurityStates.Quarantined,
            x => x.CreatedAt,
            pageSize: 1,
            cancellationToken: ct)).SingleOrDefault();
        return new AttachmentSecurityStatus(
            quarantined,
            clean,
            rejected,
            oldest?.CreatedAt,
            clock.UtcNow);
    }

    public async Task<AttachmentMaintenanceResult> RunBatchAsync(
        string? organizationId,
        CancellationToken ct)
    {
        organizationId = string.IsNullOrWhiteSpace(organizationId)
            ? null
            : RequiredOrganizationId(organizationId);
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
            x => (organizationId == null || x.OrganizationId == organizationId)
                && x.SecurityState == AttachmentSecurityStates.Quarantined,
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
            x => (organizationId == null || x.OrganizationId == organizationId)
                && x.SecurityState == AttachmentSecurityStates.Rejected
                && x.CreatedAt <= rejectedCutoff,
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

        if (organizationId is null)
        {
            var objects = await storage.ListObjectsAsync(batchSize, ct);
            foreach (var storedObject in objects.Where(x => x.CreatedAt <= orphanCutoff))
            {
                if (!await attachments.ExistsByFilterAsync(x => x.StoragePath == storedObject.StoragePath, ct))
                {
                    await storage.DeleteAsync(storedObject.StoragePath, ct);
                    deletedOrphans++;
                }
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

    private static string RequiredOrganizationId(string value) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ValidationException("Attachment security organization id is required.");
}
