namespace Zumbo.Modules.WorkItems;

public sealed record IntakeAttachmentUpload(
    string FieldKey,
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes);
