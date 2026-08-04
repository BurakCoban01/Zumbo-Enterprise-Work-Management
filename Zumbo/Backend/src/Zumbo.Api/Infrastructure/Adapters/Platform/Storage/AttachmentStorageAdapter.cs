using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Storage;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class AttachmentStorageAdapter(
    IFileStorage storage,
    IAttachmentMalwareScanner malwareScanner,
    IOptions<AttachmentSecurityOptions> options,
    IClock clock,
    ILogger<AttachmentStorageAdapter>? logger = null) : IAttachmentStorage
{
    private const long MaximumAttachmentBytes = 25 * 1024 * 1024;

    public async Task<StoredAttachment> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken ct)
    {
        MemoryStream? buffered = null;
        StoredFile? quarantined = null;
        try
        {
            var inspected = await AttachmentContentInspector.InspectAsync(
                content,
                fileName,
                contentType,
                Math.Min(maxSizeBytes, MaximumAttachmentBytes),
                options.Value,
                ct);
            buffered = inspected.BufferedContent;
            quarantined = await storage.SaveQuarantinedAsync(
                inspected.BufferedContent,
                inspected.FileName,
                inspected.ContentType,
                Math.Min(maxSizeBytes, MaximumAttachmentBytes),
                ct);
            inspected.BufferedContent.Position = 0;
            var scan = await ScanFailClosedAsync(inspected.BufferedContent, inspected.FileName, ct);
            return await ApplyInitialScanOutcomeAsync(quarantined, scan, ct);
        }
        catch (InvalidDataException)
        {
            if (quarantined is not null)
            {
                await TryDeleteQuarantineAsync(quarantined.StoragePath);
            }
            throw new ValidationException("Attachment size cannot exceed 25 MB.");
        }
        catch
        {
            if (quarantined is not null)
            {
                await TryDeleteQuarantineAsync(quarantined.StoragePath);
            }
            throw;
        }
        finally
        {
            if (buffered is not null)
            {
                await buffered.DisposeAsync();
            }
        }
    }

    public async Task<StoredAttachment> ReprocessAsync(
        StoredAttachment attachment,
        CancellationToken ct)
    {
        await using var content = await ReadAndVerifyAsync(
            attachment.StoragePath,
            attachment.ContentType,
            attachment.ChecksumSha256,
            ct);
        var scan = await ScanFailClosedAsync(content, attachment.FileName, ct);
        if (scan.Status == AttachmentMalwareScanStatuses.Unavailable)
        {
            return attachment with
            {
                ScanProvider = scan.Provider,
                ScanDetail = scan.Detail,
                ScannedAt = clock.UtcNow
            };
        }

        if (scan.Status == AttachmentMalwareScanStatuses.Infected)
        {
            await storage.DeleteAsync(attachment.StoragePath, ct);
            return attachment with
            {
                StoragePath = string.Empty,
                SecurityState = AttachmentSecurityStates.Rejected,
                ScanProvider = scan.Provider,
                ScanDetail = "Malware detected.",
                ScannedAt = clock.UtcNow
            };
        }

        var promoted = await storage.PromoteAsync(ToStoredFile(attachment), ct);
        return attachment with
        {
            StoragePath = promoted.StoragePath,
            SecurityState = AttachmentSecurityStates.Clean,
            ScanProvider = scan.Provider,
            ScanDetail = null,
            ScannedAt = clock.UtcNow
        };
    }

    public async Task<Stream> OpenReadAsync(
        string storagePath,
        string contentType,
        string expectedChecksumSha256,
        CancellationToken ct)
    {
        try
        {
            return await ReadAndVerifyAsync(storagePath, contentType, expectedChecksumSha256, ct);
        }
        catch (FileNotFoundException)
        {
            throw new NotFoundException(
                "ATTACHMENT_CONTENT_NOT_FOUND",
                "Attachment content was not found in storage.");
        }
    }

    public async Task<IReadOnlyList<StoredAttachmentObject>> ListObjectsAsync(
        int maxCount,
        CancellationToken ct) =>
        (await storage.ListAttachmentObjectsAsync(Math.Clamp(maxCount, 1, 1_000), ct))
        .Select(x => new StoredAttachmentObject(x.StoragePath, x.CreatedAt))
        .ToList();

    public Task DeleteAsync(string storagePath, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(storagePath)
            ? Task.CompletedTask
            : storage.DeleteAsync(storagePath, ct);

    private async Task<StoredAttachment> ApplyInitialScanOutcomeAsync(
        StoredFile quarantined,
        AttachmentMalwareScanResult scan,
        CancellationToken ct)
    {
        if (scan.Status == AttachmentMalwareScanStatuses.Infected)
        {
            throw new ValidationException("Attachment was rejected by malware scanning.");
        }

        if (scan.Status == AttachmentMalwareScanStatuses.Unavailable)
        {
            return ToAttachment(
                quarantined,
                AttachmentSecurityStates.Quarantined,
                scan.Provider,
                scan.Detail,
                clock.UtcNow);
        }

        var promoted = await storage.PromoteAsync(quarantined, ct);
        return ToAttachment(
            promoted,
            AttachmentSecurityStates.Clean,
            scan.Provider,
            null,
            clock.UtcNow);
    }

    private async Task TryDeleteQuarantineAsync(string storagePath)
    {
        var result = await CompensationExecution.RunAsync(
            "attachment.quarantine.delete",
            token => storage.DeleteAsync(storagePath, token));
        if (!result.Succeeded)
        {
            logger?.LogWarning(
                "Compensation operation {Operation} ended with {Outcome}; failure type {FailureType}.",
                result.Operation,
                result.Outcome,
                result.Exception?.GetType().Name ?? "none");
        }
    }

    private async Task<AttachmentMalwareScanResult> ScanFailClosedAsync(
        Stream content,
        string fileName,
        CancellationToken ct)
    {
        try
        {
            content.Position = 0;
            var result = await malwareScanner.ScanAsync(content, fileName, ct);
            return result.Status is AttachmentMalwareScanStatuses.Clean
                or AttachmentMalwareScanStatuses.Infected
                or AttachmentMalwareScanStatuses.Unavailable
                ? result
                : new AttachmentMalwareScanResult(
                    AttachmentMalwareScanStatuses.Unavailable,
                    result.Provider,
                    "Scanner returned an unsupported result.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new AttachmentMalwareScanResult(
                AttachmentMalwareScanStatuses.Unavailable,
                options.Value.ScannerProvider,
                "Scanner unavailable.");
        }
    }

    private async Task<MemoryStream> ReadAndVerifyAsync(
        string storagePath,
        string contentType,
        string expectedChecksumSha256,
        CancellationToken ct)
    {
        var opened = await storage.OpenReadAsync(storagePath, contentType, ct);
        await using var source = opened.Content;
        var buffered = new MemoryStream();
        using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long total = 0;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, ct);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > MaximumAttachmentBytes)
                {
                    throw new ConflictException(
                        "ATTACHMENT_INTEGRITY_FAILED",
                        "Stored attachment exceeds its security limit.");
                }

                checksum.AppendData(buffer, 0, read);
                await buffered.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            var actual = Convert.ToHexString(checksum.GetHashAndReset());
            if (!FixedTimeHexEquals(expectedChecksumSha256, actual))
            {
                throw new ConflictException(
                    "ATTACHMENT_INTEGRITY_FAILED",
                    "Stored attachment checksum verification failed.");
            }

            buffered.Position = 0;
            return buffered;
        }
        catch
        {
            await buffered.DisposeAsync();
            throw;
        }
    }

    private static bool FixedTimeHexEquals(string expected, string actual)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(actual));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static StoredAttachment ToAttachment(
        StoredFile file,
        string state,
        string provider,
        string? detail,
        DateTimeOffset scannedAt) =>
        new(
            file.FileName,
            file.ContentType,
            file.SizeBytes,
            file.StoragePath,
            file.ChecksumSha256,
            state,
            provider,
            detail,
            scannedAt);

    private static StoredFile ToStoredFile(StoredAttachment attachment) =>
        new(
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.StoragePath,
            attachment.ChecksumSha256);
}
