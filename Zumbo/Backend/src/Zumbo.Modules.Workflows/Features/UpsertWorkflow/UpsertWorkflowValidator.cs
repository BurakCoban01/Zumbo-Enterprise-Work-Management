using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed class UpsertWorkflowValidator
{
    public static void Validate(CreateWorkflowRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId) || request.Transitions.Count == 0)
        {
            throw new ValidationException("Project id and transitions are required.");
        }
    }
}
