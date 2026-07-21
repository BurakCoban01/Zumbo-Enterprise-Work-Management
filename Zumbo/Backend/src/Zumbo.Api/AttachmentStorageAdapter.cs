using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Storage;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class AttachmentStorageAdapter(
    IFileStorage storage,
    IAttachmentMalwareScanner malwareScanner,
    IOptions<AttachmentSecurityOptions> options,
    IClock clock) : IAttachmentStorage
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
                await storage.DeleteAsync(quarantined.StoragePath, CancellationToken.None);
            }
            throw new ValidationException("Attachment size cannot exceed 25 MB.");
        }
        catch
        {
            if (quarantined is not null)
            {
                await storage.DeleteAsync(quarantined.StoragePath, CancellationToken.None);
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
            await storage.DeleteAsync(quarantined.StoragePath, CancellationToken.None);
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

internal static class AttachmentContentInspector
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly IReadOnlyDictionary<string, string[]> AllowedExtensions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = [".pdf"],
            ["image/png"] = [".png"],
            ["image/jpeg"] = [".jpg", ".jpeg"],
            ["image/gif"] = [".gif"],
            ["image/webp"] = [".webp"],
            ["text/plain"] = [".txt", ".log", ".md"],
            ["text/markdown"] = [".md"],
            ["text/csv"] = [".csv"],
            ["application/json"] = [".json"],
            ["application/zip"] = [".zip"],
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = [".docx"],
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = [".xlsx"],
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = [".pptx"]
        };
    private static readonly HashSet<string> DangerousArchiveExtensions = new(
        [
            ".exe", ".dll", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jar",
            ".msi", ".scr", ".lnk", ".chm", ".hta", ".iso", ".img", ".zip", ".7z", ".rar"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static async Task<InspectedAttachmentContent> InspectAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        AttachmentSecurityOptions options,
        CancellationToken ct)
    {
        var normalizedType = NormalizeContentType(contentType);
        if (!AllowedExtensions.TryGetValue(normalizedType, out var extensions))
        {
            throw new ValidationException("Attachment type is not allowed.");
        }

        var safeFileName = Path.GetFileName(fileName);
        var extension = Path.GetExtension(safeFileName);
        if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException("Attachment extension does not match its content type.");
        }

        var buffered = new MemoryStream();
        try
        {
            await CopyWithLimitAsync(content, buffered, maxSizeBytes, ct);
            if (buffered.Length == 0)
            {
                throw new ValidationException("Attachment content cannot be empty.");
            }

            var bytes = buffered.ToArray();
            EnsureStructure(normalizedType, bytes, options);
            buffered.Position = 0;
            return new InspectedAttachmentContent(buffered, safeFileName, normalizedType);
        }
        catch
        {
            await buffered.DisposeAsync();
            throw;
        }
    }

    private static string NormalizeContentType(string contentType)
    {
        var normalized = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "application/octet-stream" : normalized;
    }

    private static void EnsureStructure(
        string contentType,
        byte[] bytes,
        AttachmentSecurityOptions options)
    {
        var valid = contentType switch
        {
            "application/pdf" => IsSafePdf(bytes),
            "image/png" => IsCompletePng(bytes),
            "image/jpeg" => StartsWith(bytes, [0xFF, 0xD8, 0xFF]) && EndsWith(bytes, [0xFF, 0xD9]),
            "image/gif" => (StartsWith(bytes, "GIF87a"u8) || StartsWith(bytes, "GIF89a"u8))
                && bytes[^1] == 0x3B,
            "image/webp" => IsCompleteWebp(bytes),
            "application/zip" => IsSafeArchive(bytes, null, options),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" =>
                IsSafeArchive(bytes, "word/", options),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" =>
                IsSafeArchive(bytes, "xl/", options),
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" =>
                IsSafeArchive(bytes, "ppt/", options),
            "text/plain" or "text/markdown" or "text/csv" => IsUtf8Text(bytes),
            "application/json" => IsJson(bytes),
            _ => false
        };

        if (!valid)
        {
            throw new ValidationException(
                "Attachment content is malformed, unsafe, or does not match its declared type.");
        }
    }

    private static bool IsCompletePng(ReadOnlySpan<byte> bytes)
    {
        if (!StartsWith(bytes, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return false;
        }

        var offset = 8;
        while (offset + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(offset, 4));
            if (length < 0 || offset + 12L + length > bytes.Length)
            {
                return false;
            }

            var type = bytes.Slice(offset + 4, 4);
            offset += 12 + length;
            if (type.SequenceEqual("IEND"u8))
            {
                return length == 0 && offset == bytes.Length;
            }
        }

        return false;
    }

    private static bool IsCompleteWebp(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12
        && StartsWith(bytes, "RIFF"u8)
        && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)
        && BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) + 8 == bytes.Length;

    private static bool IsSafePdf(byte[] bytes)
    {
        if (!StartsWith(bytes, "%PDF-"u8))
        {
            return false;
        }

        var text = Encoding.Latin1.GetString(bytes);
        if (new[] { "/JavaScript", "/JS", "/OpenAction", "/Launch", "/EmbeddedFile" }
            .Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var eof = text.LastIndexOf("%%EOF", StringComparison.Ordinal);
        return eof >= 0 && text[(eof + 5)..].All(char.IsWhiteSpace);
    }

    private static bool IsSafeArchive(
        byte[] bytes,
        string? requiredOfficeRoot,
        AttachmentSecurityOptions options)
    {
        if (!StartsWith(bytes, [0x50, 0x4B, 0x03, 0x04]) || !HasExactZipTerminator(bytes))
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count == 0
                || archive.Entries.Count > Math.Clamp(options.MaxArchiveEntries, 1, 10_000))
            {
                return false;
            }

            if (requiredOfficeRoot is not null
                && (!archive.Entries.Any(x => x.FullName == "[Content_Types].xml")
                    || !archive.Entries.Any(x => x.FullName.StartsWith(requiredOfficeRoot, StringComparison.OrdinalIgnoreCase))))
            {
                return false;
            }

            long expanded = 0;
            var expandedLimit = Math.Clamp(
                options.MaxArchiveExpandedBytes,
                1 * 1024 * 1024,
                500L * 1024 * 1024);
            var ratioLimit = Math.Clamp(options.MaxArchiveCompressionRatio, 2, 1_000);
            var buffer = new byte[64 * 1024];
            foreach (var entry in archive.Entries)
            {
                if (!IsSafeArchivePath(entry.FullName)
                    || entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.Contains("macrosheets/", StringComparison.OrdinalIgnoreCase)
                    || DangerousArchiveExtensions.Contains(Path.GetExtension(entry.FullName)))
                {
                    return false;
                }

                if (entry.Length > 0
                    && (entry.CompressedLength == 0 || entry.Length / Math.Max(1, entry.CompressedLength) > ratioLimit))
                {
                    return false;
                }

                using var entryStream = entry.Open();
                while (true)
                {
                    var read = entryStream.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    expanded += read;
                    if (expanded > expandedLimit)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool HasExactZipTerminator(ReadOnlySpan<byte> bytes)
    {
        var start = Math.Max(0, bytes.Length - (65_535 + 22));
        for (var index = bytes.Length - 22; index >= start; index--)
        {
            if (!bytes.Slice(index, 4).SequenceEqual(new byte[] { 0x50, 0x4B, 0x05, 0x06 }))
            {
                continue;
            }

            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(index + 20, 2));
            return index + 22 + commentLength == bytes.Length;
        }

        return false;
    }

    private static bool IsSafeArchivePath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && !path.StartsWith('/')
        && !path.StartsWith('\\')
        && !Path.IsPathRooted(path)
        && !path.Split('/', '\\').Any(segment => segment is ".." || segment.Any(char.IsControl));

    private static bool IsUtf8Text(byte[] bytes)
    {
        if (bytes.Contains((byte)0))
        {
            return false;
        }

        try
        {
            _ = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsJson(byte[] bytes)
    {
        if (!IsUtf8Text(bytes))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool StartsWith(ReadOnlySpan<byte> source, ReadOnlySpan<byte> signature) =>
        source.Length >= signature.Length && source[..signature.Length].SequenceEqual(signature);

    private static bool EndsWith(ReadOnlySpan<byte> source, ReadOnlySpan<byte> signature) =>
        source.Length >= signature.Length && source[^signature.Length..].SequenceEqual(signature);

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maxSizeBytes,
        CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0)
            {
                return;
            }

            copied += read;
            if (copied > maxSizeBytes)
            {
                throw new InvalidDataException("Attachment exceeds the configured storage limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}

internal sealed record InspectedAttachmentContent(
    MemoryStream BufferedContent,
    string FileName,
    string ContentType);
