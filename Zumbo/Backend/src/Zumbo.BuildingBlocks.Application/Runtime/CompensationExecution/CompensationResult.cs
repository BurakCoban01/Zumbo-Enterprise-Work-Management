using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Zumbo.BuildingBlocks.Application.Runtime;

public sealed record CompensationResult(
    string Operation,
    CompensationOutcome Outcome,
    TimeSpan Duration,
    Exception? Exception)
{
    public bool Succeeded => Outcome == CompensationOutcome.Succeeded;
}
