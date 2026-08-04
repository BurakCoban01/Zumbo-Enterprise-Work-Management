namespace Zumbo.Modules.WorkItems;

public interface IWorkItemBulkArtifactStorage
{
    Task<StoredWorkItemBulkArtifact> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken ct);

    Task<Stream> OpenReadAsync(
        string storagePath,
        string contentType,
        string expectedChecksumSha256,
        long maxSizeBytes,
        CancellationToken ct);

    Task DeleteAsync(string storagePath, CancellationToken ct);
}
