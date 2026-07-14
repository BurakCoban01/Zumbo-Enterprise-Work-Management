using Zumbo.BuildingBlocks.Infrastructure.Storage;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;
using System.Text;

public sealed class AttachmentStorageAdapter(IFileStorage storage) : IAttachmentStorage
{
    public async Task<StoredAttachment> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken ct)
    {
        MemoryStream? buffered = null;
        try
        {
            var inspected = await AttachmentContentInspector.InspectAsync(
                content,
                fileName,
                contentType,
                maxSizeBytes,
                ct);
            buffered = inspected.BufferedContent;
            var stored = await storage.SaveAsync(
                inspected.Content,
                fileName,
                inspected.ContentType,
                maxSizeBytes,
                ct);
            return new StoredAttachment(stored.FileName, stored.ContentType, stored.SizeBytes, stored.StoragePath);
        }
        catch (InvalidDataException)
        {
            throw new ValidationException("Attachment size cannot exceed 25 MB.");
        }
        finally
        {
            if (buffered is not null)
            {
                await buffered.DisposeAsync();
            }
        }
    }

    public async Task<Stream> OpenReadAsync(string storagePath, string contentType, CancellationToken ct)
    {
        try
        {
            return (await storage.OpenReadAsync(storagePath, contentType, ct)).Content;
        }
        catch (FileNotFoundException)
        {
            throw new NotFoundException("ATTACHMENT_CONTENT_NOT_FOUND", "Attachment content was not found in storage.");
        }
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct) => storage.DeleteAsync(storagePath, ct);
}

internal static class AttachmentContentInspector
{
    private const int SignatureLength = 16;
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
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = [".pptx"],
            ["application/msword"] = [".doc"],
            ["application/vnd.ms-excel"] = [".xls"],
            ["application/vnd.ms-powerpoint"] = [".ppt"]
        };

    public static async Task<InspectedAttachmentContent> InspectAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken ct)
    {
        var normalizedType = NormalizeContentType(contentType);
        if (!AllowedExtensions.TryGetValue(normalizedType, out var extensions))
        {
            throw new ValidationException("Attachment type is not allowed.");
        }

        var extension = Path.GetExtension(Path.GetFileName(fileName));
        if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException("Attachment extension does not match its content type.");
        }

        var initialPosition = content.CanSeek ? content.Position : 0;
        var prefix = new byte[SignatureLength];
        var prefixLength = 0;
        while (prefixLength < prefix.Length)
        {
            var read = await content.ReadAsync(prefix.AsMemory(prefixLength, prefix.Length - prefixLength), ct);
            if (read == 0)
            {
                break;
            }

            prefixLength += read;
        }

        MemoryStream? buffered = null;
        Stream inspectedContent;
        if (content.CanSeek)
        {
            content.Position = initialPosition;
            inspectedContent = content;
        }
        else
        {
            buffered = new MemoryStream();
            await buffered.WriteAsync(prefix.AsMemory(0, prefixLength), ct);
            await CopyRemainderWithLimitAsync(content, buffered, maxSizeBytes - prefixLength, ct);
            buffered.Position = 0;
            inspectedContent = buffered;
        }

        try
        {
            EnsureSignature(normalizedType, prefix.AsSpan(0, prefixLength));
        }
        catch
        {
            if (buffered is not null)
            {
                await buffered.DisposeAsync();
            }
            throw;
        }

        return new InspectedAttachmentContent(inspectedContent, buffered, normalizedType);
    }

    private static string NormalizeContentType(string contentType)
    {
        var normalized = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "application/octet-stream" : normalized;
    }

    private static void EnsureSignature(string contentType, ReadOnlySpan<byte> prefix)
    {
        var valid = contentType switch
        {
            "application/pdf" => StartsWith(prefix, "%PDF-"u8),
            "image/png" => StartsWith(prefix, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            "image/jpeg" => StartsWith(prefix, [0xFF, 0xD8, 0xFF]),
            "image/gif" => StartsWith(prefix, "GIF87a"u8) || StartsWith(prefix, "GIF89a"u8),
            "image/webp" => prefix.Length >= 12
                && StartsWith(prefix, "RIFF"u8)
                && prefix.Slice(8, 4).SequenceEqual("WEBP"u8),
            "application/zip" or
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" =>
                StartsWith(prefix, [0x50, 0x4B, 0x03, 0x04])
                || StartsWith(prefix, [0x50, 0x4B, 0x05, 0x06])
                || StartsWith(prefix, [0x50, 0x4B, 0x07, 0x08]),
            "application/msword" or "application/vnd.ms-excel" or "application/vnd.ms-powerpoint" =>
                StartsWith(prefix, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
            "text/plain" or "text/markdown" or "text/csv" or "application/json" => IsUtf8Text(prefix),
            _ => false
        };

        if (!valid)
        {
            throw new ValidationException("Attachment content does not match its declared type.");
        }
    }

    private static bool StartsWith(ReadOnlySpan<byte> source, ReadOnlySpan<byte> signature) =>
        source.Length >= signature.Length && source[..signature.Length].SequenceEqual(signature);

    private static bool IsUtf8Text(ReadOnlySpan<byte> prefix)
    {
        if (prefix.IsEmpty || prefix.Contains((byte)0))
        {
            return false;
        }

        try
        {
            var decoder = StrictUtf8.GetDecoder();
            Span<char> characters = stackalloc char[SignatureLength];
            decoder.Convert(prefix, characters, flush: false, out _, out _, out _);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static async Task CopyRemainderWithLimitAsync(
        Stream source,
        Stream destination,
        long remainingBytes,
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
            if (copied > remainingBytes)
            {
                throw new InvalidDataException("Attachment exceeds the configured storage limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}

internal sealed record InspectedAttachmentContent(
    Stream Content,
    MemoryStream? BufferedContent,
    string ContentType);
