using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using System.Security.Cryptography;

namespace Zumbo.BuildingBlocks.Infrastructure.Storage;

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
