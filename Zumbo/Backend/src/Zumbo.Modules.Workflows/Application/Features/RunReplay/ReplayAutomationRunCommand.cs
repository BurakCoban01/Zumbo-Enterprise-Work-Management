namespace Zumbo.Modules.Workflows.Application.Features.RunReplay;

public sealed record ReplayAutomationRunCommand(
    string RunId,
    string CorrelationId);
