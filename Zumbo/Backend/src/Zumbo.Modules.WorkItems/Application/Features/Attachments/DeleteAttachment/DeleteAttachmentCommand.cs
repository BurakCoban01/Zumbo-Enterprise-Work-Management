namespace Zumbo.Modules.WorkItems;

public sealed record DeleteAttachmentCommand(
    string Id,
    string AttachmentId,
    string CorrelationId);
