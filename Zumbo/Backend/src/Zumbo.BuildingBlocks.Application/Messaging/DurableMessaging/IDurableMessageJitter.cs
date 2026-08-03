namespace Zumbo.BuildingBlocks.Application.Messaging;

public interface IDurableMessageJitter
{
    double NextUnit();
}
