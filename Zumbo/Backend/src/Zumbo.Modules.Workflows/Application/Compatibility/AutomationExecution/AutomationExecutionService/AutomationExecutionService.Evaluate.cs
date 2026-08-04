using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    private static bool Evaluate(
        AutomationConditionDocument condition,
        IReadOnlyDictionary<string, string?> fields)
    {
        if (condition.Kind == "All")
            return condition.Children.All(child => Evaluate(child, fields));
        if (condition.Kind == "Any")
            return condition.Children.Any(child => Evaluate(child, fields));

        var actual = fields.FirstOrDefault(pair =>
            pair.Key.Equals(condition.Field, StringComparison.OrdinalIgnoreCase)).Value;
        return condition.Operator switch
        {
            "Equals" => string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),
            "NotEquals" => !string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),
            "Contains" => actual?.Contains(condition.Value!, StringComparison.OrdinalIgnoreCase) == true,
            "NotContains" => actual?.Contains(condition.Value!, StringComparison.OrdinalIgnoreCase) != true,
            "IsEmpty" => string.IsNullOrWhiteSpace(actual),
            "IsNotEmpty" => !string.IsNullOrWhiteSpace(actual),
            _ => false
        };
    }
}
