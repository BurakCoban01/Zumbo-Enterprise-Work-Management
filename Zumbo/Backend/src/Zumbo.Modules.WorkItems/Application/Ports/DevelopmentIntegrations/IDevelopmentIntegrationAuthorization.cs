namespace Zumbo.Modules.WorkItems;

public interface IDevelopmentIntegrationAuthorization
{
    Task EnsureCanManageAsync(string organizationId, CancellationToken ct);
}
