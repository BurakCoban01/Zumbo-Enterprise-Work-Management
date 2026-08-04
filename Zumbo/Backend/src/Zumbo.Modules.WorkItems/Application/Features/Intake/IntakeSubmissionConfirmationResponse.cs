namespace Zumbo.Modules.WorkItems;

public sealed record IntakeSubmissionConfirmationResponse(
    string SubmissionId,
    string ConfirmationCode,
    string Message,
    string State,
    string? WorkItemId);
