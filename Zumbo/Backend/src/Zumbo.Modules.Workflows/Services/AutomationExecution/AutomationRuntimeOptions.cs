using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed class AutomationRuntimeOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 15;
    public int BatchSize { get; init; } = 50;
    public int MaximumScheduledSourcesPerRule { get; init; } = 1000;
}
