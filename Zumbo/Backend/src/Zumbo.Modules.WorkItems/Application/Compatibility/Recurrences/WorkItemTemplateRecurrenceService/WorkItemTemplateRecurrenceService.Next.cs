using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService{

    private static DateTimeOffset Next(DateTimeOffset value, string frequency, int interval) =>
        frequency switch
        {
            WorkItemRecurrenceFrequencies.Daily => value.AddDays(interval),
            WorkItemRecurrenceFrequencies.Weekly => value.AddDays(checked(interval * 7)),
            WorkItemRecurrenceFrequencies.Monthly => value.AddMonths(interval),
            _ => throw new InvalidOperationException("Stored recurrence frequency is invalid.")
        };
}
