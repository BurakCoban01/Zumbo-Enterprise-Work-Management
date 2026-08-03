using System.Linq.Expressions;

namespace Zumbo.BuildingBlocks.Application.Persistence;

public interface IVersionedResource
{
    long Version { get; }
}
