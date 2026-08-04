using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class IntakeSubmissionAttachmentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FieldKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public string SecurityState { get; set; } = AttachmentSecurityStates.Clean;
    public string ScanProvider { get; set; } = string.Empty;
    public string? ScanDetail { get; set; }
    public DateTimeOffset? ScannedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
