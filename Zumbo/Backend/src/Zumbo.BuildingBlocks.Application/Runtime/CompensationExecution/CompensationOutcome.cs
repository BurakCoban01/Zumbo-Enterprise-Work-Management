using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Zumbo.BuildingBlocks.Application.Runtime;

public enum CompensationOutcome
{
    Succeeded,
    Failed,
    TimedOut
}
