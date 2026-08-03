using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using System.Security.Cryptography;

namespace Zumbo.BuildingBlocks.Infrastructure.Storage;

public sealed class LocalFileStorage(IOptions<LocalStorageOptions> options) : IFileStorage
{
    public async Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken cancellationToken = default)
        => await SaveCoreAsync(
            content, fileName, contentType, maxSizeBytes, "attachments", cancellationToken);

    public async Task<StoredFile> SaveQuarantinedAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken cancellationToken = default) =>
        await SaveCoreAsync(
            content, fileName, contentType, maxSizeBytes, "quarantine", cancellationToken);

    public async Task<StoredFile> SaveArtifactAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken cancellationToken = default) =>
        await SaveCoreAsync(content, fileName, contentType, maxSizeBytes, "artifacts", cancellationToken);

    private async Task<StoredFile> SaveCoreAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        string area,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var safeFileName = SanitizeFileName(fileName);
        var storedName = $"{area}/{DateTimeOffset.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():N}-{safeFileName}";
        var root = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(root);
        var path = ResolvePath(root, storedName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            await using var fileStream = File.Create(path);
            var copy = await CopyWithLimitAsync(content, fileStream, maxSizeBytes, cancellationToken);
            return new StoredFile(
                safeFileName,
                NormalizeContentType(contentType),
                copy.SizeBytes,
                storedName,
                copy.ChecksumSha256);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    public Task<StoredFile> PromoteAsync(
        StoredFile quarantinedFile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!quarantinedFile.StoragePath.StartsWith("quarantine/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only quarantined attachment objects can be promoted.");
        }

        var root = Path.GetFullPath(options.Value.RootPath);
        var source = ResolvePath(root, quarantinedFile.StoragePath);
        var promotedKey = "attachments/" + quarantinedFile.StoragePath["quarantine/".Length..];
        var destination = ResolvePath(root, promotedKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(source))
        {
            File.Move(source, destination, overwrite: false);
        }
        else if (!File.Exists(destination))
        {
            throw new FileNotFoundException("Quarantined attachment was not found.", quarantinedFile.StoragePath);
        }

        return Task.FromResult(quarantinedFile with { StoragePath = promotedKey });
    }

    public Task<StoredFileContent> OpenReadAsync(
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(options.Value.RootPath);
        var path = ResolvePath(root, storagePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Stored attachment was not found.", storagePath);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        return Task.FromResult(new StoredFileContent(stream, NormalizeContentType(contentType), stream.Length));
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(options.Value.RootPath);
        File.Delete(ResolvePath(root, storagePath));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredFileObject>> ListAttachmentObjectsAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var limit = Math.Clamp(maxCount, 1, 1_000);
        var root = Path.GetFullPath(options.Value.RootPath);
        if (!Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<StoredFileObject>>([]);
        }

        var result = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new StoredFileObject(
                Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.GetLastWriteTimeUtc(path)))
            .Where(x =>
                x.StoragePath.StartsWith("attachments/", StringComparison.Ordinal)
                || x.StoragePath.StartsWith("quarantine/", StringComparison.Ordinal))
            .OrderBy(x => x.CreatedAt)
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<StoredFileObject>>(result);
    }

    public async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(root);
        var probe = ResolvePath(root, $".health-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllTextAsync(probe, "ready", cancellationToken);
        }
        finally
        {
            File.Delete(probe);
        }
    }

    private static string ResolvePath(string root, string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || Path.IsPathRooted(storagePath))
        {
            throw new InvalidOperationException("Storage path must be a non-empty relative key.");
        }

        var path = Path.GetFullPath(Path.Combine(root, storagePath));
        var relativePath = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Storage path escapes the configured root.");
        }

        return path;
    }

    internal static string SanitizeFileName(string fileName)
    {
        var leaf = Path.GetFileName(fileName ?? string.Empty);
        var safe = string.Join("_", leaf.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "attachment.bin" : safe[..Math.Min(safe.Length, 180)];
    }

    internal static string NormalizeContentType(string contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim()[..Math.Min(contentType.Trim().Length, 128)];

    internal static async Task<FileCopyResult> CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maxSizeBytes,
        CancellationToken cancellationToken)
    {
        if (maxSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSizeBytes));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var buffer = new byte[64 * 1024];
        using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return new FileCopyResult(total, Convert.ToHexString(checksum.GetHashAndReset()).ToLowerInvariant());
            }

            total += read;
            if (total > maxSizeBytes)
            {
                throw new InvalidDataException($"File exceeds the {maxSizeBytes} byte storage limit.");
            }

            checksum.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
