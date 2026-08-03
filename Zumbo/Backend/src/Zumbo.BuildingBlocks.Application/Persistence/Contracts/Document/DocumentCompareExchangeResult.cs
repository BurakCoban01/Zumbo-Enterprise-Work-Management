using System.Linq.Expressions;

namespace Zumbo.BuildingBlocks.Application.Persistence;

public readonly record struct DocumentCompareExchangeResult(
    long MatchedCount,
    long ModifiedCount,
    long? Version)
{
    public bool Found => MatchedCount > 0;
    public bool Changed => ModifiedCount > 0;
}
