using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    private static DateTimeOffset NextScheduleAfter(
        DateTimeOffset scheduledFor,
        int intervalMinutes,
        DateTimeOffset now)
    {
        var elapsedMinutes = Math.Max(0, (now - scheduledFor).TotalMinutes);
        var intervals = Math.Floor(elapsedMinutes / intervalMinutes) + 1;
        return scheduledFor.AddMinutes(intervals * intervalMinutes);
    }
}
