using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    private static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromMinutes(Math.Min(Math.Pow(2, Math.Max(attempt - 1, 0)), 15));
}
