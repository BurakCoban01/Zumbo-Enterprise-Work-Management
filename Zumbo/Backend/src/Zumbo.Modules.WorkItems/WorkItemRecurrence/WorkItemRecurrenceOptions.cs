using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemRecurrenceOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 30;
    public int BatchSize { get; init; } = 50;
    public int MaximumOccurrences { get; init; } = 1_000;
    public int MaximumScheduleYears { get; init; } = 5;
}
