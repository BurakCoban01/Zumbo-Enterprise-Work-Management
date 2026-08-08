namespace Zumbo.Modules.WorkItems;

public sealed record UploadAttachmentCommand(
    string Id,
    Stream Content,
    string FileName,
    string ContentType,
    long DeclaredSizeBytes,
    string CorrelationId);
