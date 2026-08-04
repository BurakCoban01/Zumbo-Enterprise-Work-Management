namespace Zumbo.Modules.Workflows.Application.Features.RunResume;

public sealed record ResumeAutomationRunCommand(
    string RunId,
    bool ActorAvailable);
