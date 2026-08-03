using System.Linq.Expressions;

namespace Zumbo.BuildingBlocks.Application.Persistence;

public interface IExpectedVersionAccessor
{
    long? ExpectedVersion { get; }
}
