namespace Zumbo.Modules.WorkItems;

public sealed record AddCommentCommand(
    string Id,
    AddCommentRequest Request,
    string CorrelationId);
