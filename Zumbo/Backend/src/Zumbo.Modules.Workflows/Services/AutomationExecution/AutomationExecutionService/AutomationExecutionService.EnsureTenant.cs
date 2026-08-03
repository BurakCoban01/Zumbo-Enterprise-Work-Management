using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    private static void EnsureTenant(AutomationRunDocument run, string organizationId)
    {
        if (!run.OrganizationId.Equals(organizationId, StringComparison.Ordinal))
            throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");
    }
}
