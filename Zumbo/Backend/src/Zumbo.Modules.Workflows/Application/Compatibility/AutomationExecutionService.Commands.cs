using Zumbo.Modules.Workflows.Application.Features.RunExecution;
using Zumbo.Modules.Workflows.Application.Features.RunReplay;
using Zumbo.Modules.Workflows.Application.Features.RunResume;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService
{
    public async Task<IReadOnlyCollection<AutomationRunResponse>> ExecuteAsync(
        AutomationExecutionContext context,
        CancellationToken ct)
        => await executeAutomationHandler.HandleAsync(
            new ExecuteAutomationCommand(context),
            ct);

    public async Task<AutomationRunResponse> ReplayAsync(
        string runId,
        string correlationId,
        CancellationToken ct)
        => await replayAutomationRunHandler.HandleAsync(
            new ReplayAutomationRunCommand(runId, correlationId),
            ct);

    public async Task<AutomationRunResponse> ResumeAsync(
        string runId,
        bool actorAvailable,
        CancellationToken ct)
        => await resumeAutomationRunHandler.HandleAsync(
            new ResumeAutomationRunCommand(runId, actorAvailable),
            ct);
}
