using Microsoft.AspNetCore.Mvc;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;

using static ApiEndpointResults;

internal static class PortfolioEndpoints
{
    internal static IServiceCollection AddPortfolioModule(this IServiceCollection services)
    {
        services.AddScoped<IPortfolioDirectory, PortfolioDirectoryAdapter>();
        services.AddScoped<IPortfolioAuditWriter, PortfolioAuditWriterAdapter>();
        services.AddScoped<PortfolioService>();
        return services;
    }

    internal static void MapPortfolioEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/portfolios")
            .WithTags("Portfolios")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProjectView);

        group.MapGet("", async (
            bool? includeArchived,
            int? page,
            int? pageSize,
            [FromServices] PortfolioService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListAsync(
                includeArchived ?? false,
                page ?? 1,
                pageSize ?? 50,
                ct), http));

        group.MapGet("/{portfolioId}", async (
            string portfolioId,
            bool? includeArchived,
            [FromServices] PortfolioService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetAsync(portfolioId, includeArchived ?? false, ct), http));

        group.MapPost("", async (
            SavePortfolioRequest request,
            [FromServices] PortfolioService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveAsync(null, request, CorrelationId(http), ct), http));

        group.MapPut("/{portfolioId}", async (
            string portfolioId,
            SavePortfolioRequest request,
            [FromServices] PortfolioService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveAsync(portfolioId, request, CorrelationId(http), ct), http));

        group.MapPost("/{portfolioId}/initiatives", async (
            string portfolioId,
            SaveInitiativeRequest request,
            [FromServices] PortfolioService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveInitiativeAsync(
                portfolioId, null, request, CorrelationId(http), ct), http));

        group.MapPut("/{portfolioId}/initiatives/{initiativeId}", async (
            string portfolioId,
            string initiativeId,
            SaveInitiativeRequest request,
            [FromServices] PortfolioService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveInitiativeAsync(
                portfolioId, initiativeId, request, CorrelationId(http), ct), http));

        group.MapPost("/{portfolioId}/initiatives/{initiativeId}/status-updates", async (
            string portfolioId,
            string initiativeId,
            AddInitiativeStatusUpdateRequest request,
            [FromServices] PortfolioService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.AddStatusUpdateAsync(
                portfolioId, initiativeId, request, CorrelationId(http), ct), http));

        group.MapPost("/{portfolioId}/dependencies", async (
            string portfolioId,
            SavePortfolioDependencyRequest request,
            [FromServices] PortfolioService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveDependencyAsync(
                portfolioId, null, request, CorrelationId(http), ct), http));

        group.MapPut("/{portfolioId}/dependencies/{dependencyId}", async (
            string portfolioId,
            string dependencyId,
            SavePortfolioDependencyRequest request,
            [FromServices] PortfolioService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveDependencyAsync(
                portfolioId, dependencyId, request, CorrelationId(http), ct), http));

        group.MapGet("/{portfolioId}/roadmap", async (
            string portfolioId,
            [FromServices] PortfolioService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetRoadmapAsync(portfolioId, ct), http));

        group.MapDelete("/{portfolioId}", async (
            string portfolioId,
            [FromServices] PortfolioService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.ArchiveAsync(portfolioId, CorrelationId(http), ct);
            return Ok(new { archived = true }, http);
        });
    }
}
