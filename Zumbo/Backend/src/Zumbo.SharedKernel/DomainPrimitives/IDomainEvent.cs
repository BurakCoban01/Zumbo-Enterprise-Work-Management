namespace Zumbo.SharedKernel;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
