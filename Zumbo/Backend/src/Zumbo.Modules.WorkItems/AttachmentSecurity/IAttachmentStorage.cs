using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

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
