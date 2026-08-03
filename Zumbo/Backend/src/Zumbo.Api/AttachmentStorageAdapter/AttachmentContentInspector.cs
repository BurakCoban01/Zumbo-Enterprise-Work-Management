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
