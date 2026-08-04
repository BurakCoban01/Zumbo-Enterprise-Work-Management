using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    private static bool TriggerMatches(
        AutomationTriggerDocument trigger,
        AutomationExecutionContext context) =>
        trigger.Type.Equals(context.TriggerType, StringComparison.OrdinalIgnoreCase)
        && (trigger.Type == "Schedule"
            || trigger.EventType!.Equals(context.EventType, StringComparison.OrdinalIgnoreCase));
}
