using System.Security.Cryptography;
using Zumbo.BuildingBlocks.Infrastructure.Storage;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class WorkItemBulkArtifactStorageAdapter(IFileStorage storage) : IWorkItemBulkArtifactStorage
{
    public async Task<StoredWorkItemBulkArtifact> SaveAsync(
        Stream content, string fileName, string contentType, long maxSizeBytes, CancellationToken ct)
    {
        var stored = await storage.SaveArtifactAsync(content, fileName, contentType, maxSizeBytes, ct);
        return new StoredWorkItemBulkArtifact(
            stored.FileName, stored.ContentType, stored.SizeBytes, stored.StoragePath, stored.ChecksumSha256);
    }

    public async Task<Stream> OpenReadAsync(
        string storagePath, string contentType, string expectedChecksumSha256,
        long maxSizeBytes, CancellationToken ct)
    {
        try
        {
            var opened = await storage.OpenReadAsync(storagePath, contentType, ct);
            await using var source = opened.Content;
            var result = new MemoryStream();
            using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, ct);
                if (read == 0) break;
                total += read;
                if (total > maxSizeBytes)
                    throw new ConflictException("WORK_ITEM_BULK_ARTIFACT_INVALID", "Stored job artifact exceeds its limit.");
                checksum.AppendData(buffer, 0, read);
                await result.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            var actual = checksum.GetHashAndReset();
            byte[] expected;
            try { expected = Convert.FromHexString(expectedChecksumSha256); }
            catch (FormatException)
            {
                throw new ConflictException("WORK_ITEM_BULK_ARTIFACT_INVALID", "Stored job artifact checksum is invalid.");
            }
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                throw new ConflictException("WORK_ITEM_BULK_ARTIFACT_INVALID", "Stored job artifact integrity check failed.");
            result.Position = 0;
            return result;
        }
        catch (FileNotFoundException)
        {
            throw new NotFoundException("WORK_ITEM_BULK_ARTIFACT_NOT_FOUND", "Job artifact content was not found.");
        }
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct) => storage.DeleteAsync(storagePath, ct);
}
