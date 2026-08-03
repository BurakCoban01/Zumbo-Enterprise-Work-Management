namespace Zumbo.SharedKernel;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
