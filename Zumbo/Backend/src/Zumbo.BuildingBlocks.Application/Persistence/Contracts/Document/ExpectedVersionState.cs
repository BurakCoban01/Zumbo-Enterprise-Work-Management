using System.Linq.Expressions;

namespace Zumbo.BuildingBlocks.Application.Persistence;

public sealed class ExpectedVersionState(IExpectedVersionAccessor? accessor)
{
    private bool consumed;

    public long Consume(long currentVersion)
    {
        if (consumed || accessor?.ExpectedVersion is not long expectedVersion)
        {
            return currentVersion;
        }

        consumed = true;
        return expectedVersion;
    }
}
