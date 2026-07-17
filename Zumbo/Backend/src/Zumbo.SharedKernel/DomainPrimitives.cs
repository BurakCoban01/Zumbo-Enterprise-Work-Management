namespace Zumbo.SharedKernel;

public abstract class Entity
{
    public string Id { get; protected init; } = Guid.NewGuid().ToString("N");
}

public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public abstract record ValueObject;

public sealed record ApiError(string Code, string Message);

public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    ApiError? Error,
    string CorrelationId)
{
    public static ApiResponse<T> Ok(T data, string correlationId) =>
        new(true, data, null, correlationId);

    public static ApiResponse<T> Fail(string code, string message, string correlationId) =>
        new(false, default, new ApiError(code, message), correlationId);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ICurrentUser
{
    string? UserId { get; }
    string? OrganizationId { get; }
    IReadOnlyCollection<string> Roles { get; }
}
