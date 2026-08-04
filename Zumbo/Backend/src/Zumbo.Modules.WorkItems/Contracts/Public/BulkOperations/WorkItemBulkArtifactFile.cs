namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemBulkArtifactFile(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes);
