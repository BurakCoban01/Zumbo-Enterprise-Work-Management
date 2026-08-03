using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed class GetWorkflowValidator
{
    public static void Validate(GetWorkflowQuery query) => ArgumentNullException.ThrowIfNull(query);
}
