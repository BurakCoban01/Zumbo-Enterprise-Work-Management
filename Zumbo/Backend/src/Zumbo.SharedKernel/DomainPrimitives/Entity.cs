namespace Zumbo.SharedKernel;

public abstract class Entity
{
    public string Id { get; protected init; } = Guid.NewGuid().ToString("N");
}
