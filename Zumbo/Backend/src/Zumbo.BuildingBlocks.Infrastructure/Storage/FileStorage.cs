using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using System.Security.Cryptography;

namespace Zumbo.BuildingBlocks.Infrastructure.Storage;

public sealed class LocalStorageOptions
{
    public string RootPath { get; init; } = "storage";
    public string PublicBasePath { get; init; } = "/files";
}

public sealed class StorageOptions
{
    public string Provider { get; init; } = "Local";
}

public sealed class MinioStorageOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string BucketName { get; init; } = "zumbo-attachments";
    public string PublicBaseUrl { get; init; } = string.Empty;
    public int RequestTimeoutSeconds { get; init; } = 10;
}

public sealed record StoredFile(
    string FileName,
    string ContentType,
    long SizeBytes,
    string StoragePath,
    string ChecksumSha256);
public sealed record StoredFileContent(Stream Content, string ContentType, long SizeBytes);
public sealed record StoredFileObject(string StoragePath, DateTimeOffset CreatedAt);

public static class StorageConfiguration
{
    public static string GetValidatedProvider(IConfiguration configuration)
    {
        var provider = configuration["Storage:Provider"]?.Trim();
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException("Storage:Provider must be configured as Local or Minio.");
        }

        if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(configuration["Storage:Local:RootPath"]))
            {
                throw new InvalidOperationException("Storage:Local:RootPath must be configured for the Local provider.");
            }

            return "Local";
        }

        if (!provider.Equals("Minio", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Storage provider '{provider}' is not supported.");
        }

        var endpoint = configuration["Storage:Minio:Endpoint"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Storage:Minio:Endpoint must be an absolute HTTP or HTTPS URL.");
        }

        Require(configuration, "Storage:Minio:AccessKey");
        Require(configuration, "Storage:Minio:SecretKey");
        var bucketName = Require(configuration, "Storage:Minio:BucketName");
        if (bucketName.Length is < 3 or > 63
            || bucketName.StartsWith('-')
            || bucketName.EndsWith('-')
            || bucketName.Any(character => !(char.IsLower(character) || char.IsDigit(character) || character is '-' or '.')))
        {
            throw new InvalidOperationException("Storage:Minio:BucketName must use a valid lowercase S3 bucket name.");
        }

        var timeout = configuration.GetValue<int?>("Storage:Minio:RequestTimeoutSeconds") ?? 10;
        if (timeout is < 1 or > 120)
        {
            throw new InvalidOperationException("Storage:Minio:RequestTimeoutSeconds must be between 1 and 120.");
        }

        return "Minio";
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} must be configured for the Minio provider.");
        }

        return value;
    }
}

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken cancellationToken = default);

    Task<StoredFile> SaveQuarantinedAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken cancellationToken = default);

    Task<StoredFile> SaveArtifactAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken cancellationToken = default);

    Task<StoredFile> PromoteAsync(
        StoredFile quarantinedFile,
        CancellationToken cancellationToken = default);

    Task<StoredFileContent> OpenReadAsync(
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredFileObject>> ListAttachmentObjectsAsync(
        int maxCount,
        CancellationToken cancellationToken = default);
    Task CheckHealthAsync(CancellationToken cancellationToken = default);
}

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

internal sealed record FileCopyResult(long SizeBytes, string ChecksumSha256);

public sealed class MinioFileStorage : IFileStorage
{
    private readonly MinioStorageOptions _options;
    private readonly IExternalDependencyPolicy? _resiliencePolicy;

    public MinioFileStorage(IOptions<MinioStorageOptions> options)
        : this(options, null)
    {
    }

    public MinioFileStorage(
        IOptions<MinioStorageOptions> options,
        IExternalDependencyPolicyProvider? policyProvider)
    {
        _options = options.Value;
        _resiliencePolicy = policyProvider?.Get(ExternalDependencyNames.Minio);
    }

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
        var safeFileName = LocalFileStorage.SanitizeFileName(fileName);
        var normalizedContentType = LocalFileStorage.NormalizeContentType(contentType);
        var objectName = $"{area}/{DateTimeOffset.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():N}-{safeFileName}";
        var seekable = new MemoryStream();
        var copy = await LocalFileStorage.CopyWithLimitAsync(content, seekable, maxSizeBytes, cancellationToken);
        await ExecuteAsync(
            "object-save",
            ExternalDependencyOperationKind.IdempotentWrite,
            async token =>
            {
                seekable.Position = 0;
                using var client = CreateClient();
                if (!await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(client, _options.BucketName))
                {
                    await client.PutBucketAsync(
                        new PutBucketRequest { BucketName = _options.BucketName }, token);
                }

                var request = new PutObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = objectName,
                    InputStream = seekable,
                    ContentType = normalizedContentType,
                    AutoCloseStream = false
                };
                request.Metadata["sha256"] = copy.ChecksumSha256;
                await client.PutObjectAsync(request, token);
            },
            cancellationToken);

        return new StoredFile(safeFileName, normalizedContentType, copy.SizeBytes, objectName, copy.ChecksumSha256);
    }

    public async Task<StoredFile> PromoteAsync(
        StoredFile quarantinedFile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateObjectName(quarantinedFile.StoragePath);
        if (!quarantinedFile.StoragePath.StartsWith("quarantine/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only quarantined attachment objects can be promoted.");
        }

        var promotedKey = "attachments/" + quarantinedFile.StoragePath["quarantine/".Length..];
        try
        {
            await ExecuteAsync(
                "object-promote",
                ExternalDependencyOperationKind.IdempotentWrite,
                async token =>
                {
                    using var client = CreateClient();
                    await client.CopyObjectAsync(new CopyObjectRequest
                    {
                        SourceBucket = _options.BucketName,
                        SourceKey = quarantinedFile.StoragePath,
                        DestinationBucket = _options.BucketName,
                        DestinationKey = promotedKey,
                        MetadataDirective = S3MetadataDirective.COPY
                    }, token);
                    await client.DeleteObjectAsync(_options.BucketName, quarantinedFile.StoragePath, token);
                },
                cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await ExecuteAsync(
                "object-promote-verify",
                ExternalDependencyOperationKind.Read,
                async token =>
                {
                    using var client = CreateClient();
                    _ = await client.GetObjectMetadataAsync(_options.BucketName, promotedKey, token);
                },
                cancellationToken);
        }

        return quarantinedFile with { StoragePath = promotedKey };
    }

    public async Task<StoredFileContent> OpenReadAsync(
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateObjectName(storagePath);
        return await ExecuteAsync(
            "object-read",
            ExternalDependencyOperationKind.Read,
            async token =>
            {
                using var client = CreateClient();
                using var response = await client.GetObjectAsync(_options.BucketName, storagePath, token);
                var content = new MemoryStream();
                await response.ResponseStream.CopyToAsync(content, token);
                content.Position = 0;
                return new StoredFileContent(
                    content,
                    LocalFileStorage.NormalizeContentType(response.Headers.ContentType ?? contentType),
                    content.Length);
            },
            cancellationToken);
    }

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateObjectName(storagePath);
        await ExecuteAsync(
            "object-delete",
            ExternalDependencyOperationKind.IdempotentWrite,
            async token =>
            {
                using var client = CreateClient();
                await client.DeleteObjectAsync(_options.BucketName, storagePath, token);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<StoredFileObject>> ListAttachmentObjectsAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var limit = Math.Clamp(maxCount, 1, 1_000);
        return await ExecuteAsync(
            "object-list",
            ExternalDependencyOperationKind.Read,
            async token =>
            {
                using var client = CreateClient();
                var result = new List<StoredFileObject>(limit);
                foreach (var prefix in new[] { "attachments/", "quarantine/" })
                {
                    var response = await client.ListObjectsV2Async(new ListObjectsV2Request
                    {
                        BucketName = _options.BucketName,
                        Prefix = prefix,
                        MaxKeys = limit
                    }, token);
                    result.AddRange(response.S3Objects.Select(x =>
                        new StoredFileObject(x.Key, x.LastModified)));
                }
                return (IReadOnlyList<StoredFileObject>)result
                    .OrderBy(x => x.CreatedAt)
                    .Take(limit)
                    .ToList();
            },
            cancellationToken);
    }

    public async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ExecuteAsync(
            "health",
            ExternalDependencyOperationKind.Health,
            async token =>
            {
                using var client = CreateClient();
                _ = await client.ListBucketsAsync(token);
            },
            cancellationToken);
    }

    private static void ValidateObjectName(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath)
            || storagePath.StartsWith('/')
            || storagePath.Contains('\\')
            || storagePath.Split('/').Any(segment => segment is "." or ".." || segment.Length == 0)
            || storagePath.Any(char.IsControl))
        {
            throw new InvalidOperationException("Storage path is not a valid object key.");
        }
    }

    private AmazonS3Client CreateClient()
    {
        var credentials = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
        var config = new AmazonS3Config
        {
            ServiceURL = _options.Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = RegionEndpoint.USEast1.SystemName,
            Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds),
            MaxErrorRetry = 0
        };

        return new AmazonS3Client(credentials, config);
    }

    private Task ExecuteAsync(
        string operation,
        ExternalDependencyOperationKind kind,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken) =>
        _resiliencePolicy is null
            ? action(cancellationToken)
            : _resiliencePolicy.ExecuteAsync(operation, kind, action, IsTransient, cancellationToken);

    private Task<T> ExecuteAsync<T>(
        string operation,
        ExternalDependencyOperationKind kind,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken) =>
        _resiliencePolicy is null
            ? action(cancellationToken)
            : _resiliencePolicy.ExecuteAsync(operation, kind, action, IsTransient, cancellationToken);

    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or IOException
        || exception is AmazonS3Exception s3
            && ((int)s3.StatusCode is 408 or 429 or >= 500 || string.IsNullOrEmpty(s3.ErrorCode));
}
