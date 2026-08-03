namespace Zumbo.Modules.WorkItems;

public sealed record CreateIntakeSubmissionRequest(
    IReadOnlyCollection<IntakeSubmissionValueRequest> Values,
    string? Website = null);
