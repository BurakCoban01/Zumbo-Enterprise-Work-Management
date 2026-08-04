using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public interface IWorkflowPublicationGuard
{
    Task ValidateAsync(WorkflowPublicationCandidate candidate, CancellationToken ct);
}
