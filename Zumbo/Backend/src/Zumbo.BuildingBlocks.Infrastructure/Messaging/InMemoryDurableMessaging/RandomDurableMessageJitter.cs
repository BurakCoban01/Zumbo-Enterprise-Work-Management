using System.Collections.Concurrent;
using Zumbo.BuildingBlocks.Application.Messaging;

namespace Zumbo.BuildingBlocks.Infrastructure.Messaging;

public sealed class RandomDurableMessageJitter : IDurableMessageJitter
{
    public double NextUnit() => Random.Shared.NextDouble();
}
