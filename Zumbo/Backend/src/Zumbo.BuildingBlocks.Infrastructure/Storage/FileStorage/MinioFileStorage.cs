using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using System.Security.Cryptography;

namespace Zumbo.BuildingBlocks.Infrastructure.Storage;

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
