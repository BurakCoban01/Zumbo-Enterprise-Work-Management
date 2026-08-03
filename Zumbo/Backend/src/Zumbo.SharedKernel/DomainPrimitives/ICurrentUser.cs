namespace Zumbo.SharedKernel;

public interface ICurrentUser
{
    string? UserId { get; }
    string? OrganizationId { get; }
    IReadOnlyCollection<string> Roles { get; }
}
