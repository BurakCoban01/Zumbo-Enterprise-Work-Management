namespace Zumbo.Modules.WorkItems;

public sealed record StoredWorkItemBulkArtifact(
    string FileName,
    string ContentType,
    long SizeBytes,
    string StoragePath,
    string ChecksumSha256);
