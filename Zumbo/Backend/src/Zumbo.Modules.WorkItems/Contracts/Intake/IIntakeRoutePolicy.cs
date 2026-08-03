namespace Zumbo.Modules.WorkItems;

public interface IIntakeRoutePolicy
{
    Task<IntakeRouteAuthorization> ValidateAsync(
        string organizationId,
        string projectId,
        string boardId,
        CancellationToken ct);
}
