using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public static class WorkItemRecurrenceFrequencies
{
    public const string Daily = "Daily";
    public const string Weekly = "Weekly";
    public const string Monthly = "Monthly";

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException("Recurrence frequency is required.");
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "daily" => Daily,
            "weekly" => Weekly,
            "monthly" => Monthly,
            _ => throw new ValidationException("Recurrence frequency must be Daily, Weekly, or Monthly.")
        };
    }
}
