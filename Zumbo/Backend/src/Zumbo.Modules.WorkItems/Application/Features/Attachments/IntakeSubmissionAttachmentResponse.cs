namespace Zumbo.Modules.WorkItems;

public sealed record IntakeSubmissionAttachmentResponse(
    string Id,
    string FieldKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string SecurityState);
