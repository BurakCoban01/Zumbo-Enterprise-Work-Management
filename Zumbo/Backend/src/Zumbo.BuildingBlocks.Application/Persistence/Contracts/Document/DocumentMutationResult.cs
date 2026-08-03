using System.Linq.Expressions;

namespace Zumbo.BuildingBlocks.Application.Persistence;

public readonly record struct DocumentMutationResult(long MatchedCount, long ModifiedCount)
{
    public bool Found => MatchedCount > 0;
    public bool Changed => ModifiedCount > 0;
}
