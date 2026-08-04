using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Security;

using static ApiEndpointResults;

internal static class OperationsEndpoints
{
    internal static void MapOperationsEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/operations")
            .WithTags("Operations")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true);

        group.MapGet("/external-dependencies", (IExternalDependencyPolicyProvider policies) =>
        {
            var captured = policies.GetSnapshots().ToDictionary(x => x.Dependency, StringComparer.Ordinal);
            var dependencies = ExternalDependencyNames.All.Select(name =>
                captured.TryGetValue(name, out var snapshot)
                    ? snapshot
                    : new ExternalDependencySnapshot(
                        name, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0));
            return Results.Ok(new
            {
                status = dependencies.Any(x => x.CircuitOpen) ? "degraded" : "available",
                capturedAtUtc = DateTimeOffset.UtcNow,
                dependencies
            });
        }).RequireRateLimiting("report");

        group.MapGet("/storage/security", async (
            string organizationId,
            OperationsStorageSecurityCoordinator coordinator,
            CancellationToken ct) =>
            Results.Ok(await coordinator.GetStatusAsync(organizationId, ct)))
            .RequireRateLimiting("report");

        group.MapPost("/storage/security/maintenance", async (
            string organizationId,
            OperationsStorageSecurityCoordinator coordinator,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await coordinator.RunAsync(
                organizationId,
                CorrelationId(http),
                ct)))
            .RequireRateLimiting("bulk");
    }
}
