using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    private static void ValidateContext(AutomationExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.OrganizationId)
            || string.IsNullOrWhiteSpace(context.ProjectId)
            || string.IsNullOrWhiteSpace(context.TriggerType)
            || string.IsNullOrWhiteSpace(context.TriggerId)
            || string.IsNullOrWhiteSpace(context.SourceId)
            || string.IsNullOrWhiteSpace(context.ActorUserId)
            || string.IsNullOrWhiteSpace(context.CorrelationId)
            || context.Fields is null)
        {
            throw new ValidationException("Automation execution context is incomplete.");
        }
        if (context.ChainDepth is < 0 or > 10)
            throw new ValidationException("Automation execution chain depth is invalid.");
        if ((context.VisitedRuleIds?.Count ?? 0) > 10)
            throw new ValidationException("Automation execution visited-rule list is invalid.");
        if (context.Fields.Count > AutomationRuleDefinitionFactory.MaximumConditionNodes
            || context.Fields.Any(field =>
                string.IsNullOrWhiteSpace(field.Key)
                || field.Key.Length > 50
                || field.Value?.Length > 2000))
        {
            throw new ValidationException("Automation execution fields exceed the supported bounds.");
        }
    }
}
